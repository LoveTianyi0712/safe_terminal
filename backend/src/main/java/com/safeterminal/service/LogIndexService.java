package com.safeterminal.service;

import co.elastic.clients.elasticsearch.ElasticsearchClient;
import co.elastic.clients.elasticsearch.core.BulkRequest;
import co.elastic.clients.elasticsearch.core.bulk.BulkOperation;
import co.elastic.clients.elasticsearch.core.bulk.IndexOperation;
import com.safeterminal.domain.document.LogDocument;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.PageImpl;
import org.springframework.data.domain.Pageable;
import org.springframework.data.elasticsearch.core.ElasticsearchOperations;
import org.springframework.data.elasticsearch.core.SearchHitSupport;
import org.springframework.data.elasticsearch.core.mapping.IndexCoordinates;
import org.springframework.data.elasticsearch.core.query.Criteria;
import org.springframework.data.elasticsearch.core.query.CriteriaQuery;
import org.springframework.stereotype.Service;

import java.time.Instant;
import java.time.LocalDate;
import java.time.format.DateTimeFormatter;
import java.util.Collections;
import java.util.List;
import java.util.UUID;

@Slf4j
@Service
@RequiredArgsConstructor
public class LogIndexService {

    @Value("${safe-terminal.es.index.log-prefix:logs-}")
    private String logIndexPrefix;

    private static final DateTimeFormatter DATE_IDX = DateTimeFormatter.ofPattern("yyyy.MM.dd");

    private final ElasticsearchOperations esOperations;
    private final ElasticsearchClient     esClient;

    /**
     * 批量写入 ES，索引名按当天日期生成
     */
    public void bulkIndex(List<LogDocument> docs) {
        if (docs.isEmpty()) return;

        String index = logIndexPrefix + LocalDate.now().format(DATE_IDX);

        try {
            var ops = docs.stream()
                .map(doc -> BulkOperation.of(b -> b.index(
                    IndexOperation.of(i -> i
                        .index(index)
                        .id(UUID.randomUUID().toString())
                        .document(doc)
                    ))))
                .toList();

            var resp = esClient.bulk(BulkRequest.of(r -> r.operations(ops)));
            if (resp.errors()) {
                log.error("ES bulk index had errors, first error: {}",
                    resp.items().stream()
                        .filter(i -> i.error() != null)
                        .findFirst()
                        .map(i -> i.error().reason())
                        .orElse("unknown"));
            }
        } catch (Exception e) {
            log.error("ES bulk index failed", e);
        }
    }

    /**
     * 按条件检索日志（跨天索引通配查询，支持时间范围和分页）
     *
     * @param terminalId 按终端 ID 过滤（可选）
     * @param keyword    消息关键词全文检索（可选）
     * @param startDate  开始日期 yyyy-MM-dd（可选，包含当天 00:00:00 UTC）
     * @param endDate    结束日期 yyyy-MM-dd（可选，包含当天 23:59:59 UTC）
     * @param pageable   分页与排序参数
     * @return 分页结果，默认按 timestamp 降序
     */
    public Page<LogDocument> search(String terminalId, String keyword,
                                    String startDate, String endDate,
                                    Pageable pageable) {
        Criteria criteria = new Criteria();

        if (terminalId != null && !terminalId.isBlank()) {
            criteria = criteria.and("terminalId").is(terminalId);
        }
        if (keyword != null && !keyword.isBlank()) {
            criteria = criteria.and("message").contains(keyword);
        }
        if (startDate != null && !startDate.isBlank() && endDate != null && !endDate.isBlank()) {
            Instant from = Instant.parse(startDate + "T00:00:00.000Z");
            Instant to   = Instant.parse(endDate   + "T23:59:59.999Z");
            criteria = criteria.and("timestamp").between(from, to);
        }

        // 使用通配符索引模式跨多个按天分割的索引查询
        CriteriaQuery query = new CriteriaQuery(criteria, pageable);

        try {
            var searchPage = esOperations.searchForPage(
                query, LogDocument.class,
                IndexCoordinates.of(logIndexPrefix + "*"));

            @SuppressWarnings("unchecked")
            Page<LogDocument> result = (Page<LogDocument>) SearchHitSupport.unwrapSearchHits(searchPage);
            return result;
        } catch (Exception e) {
            log.error("ES search failed", e);
            return new PageImpl<>(Collections.emptyList(), pageable, 0);
        }
    }
}
