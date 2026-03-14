package com.safeterminal.service;

import co.elastic.clients.elasticsearch.ElasticsearchClient;
import co.elastic.clients.elasticsearch.core.BulkRequest;
import co.elastic.clients.elasticsearch.core.bulk.BulkOperation;
import co.elastic.clients.elasticsearch.core.bulk.IndexOperation;
import com.safeterminal.domain.document.LogDocument;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.data.elasticsearch.core.ElasticsearchOperations;
import org.springframework.data.elasticsearch.core.SearchHit;
import org.springframework.data.elasticsearch.core.query.Criteria;
import org.springframework.data.elasticsearch.core.query.CriteriaQuery;
import org.springframework.data.elasticsearch.core.query.Query;
import org.springframework.stereotype.Service;

import java.time.LocalDate;
import java.time.format.DateTimeFormatter;
import java.util.List;
import java.util.UUID;

@Slf4j
@Service
@RequiredArgsConstructor
public class LogIndexService {

    @Value("${safe-terminal.es.index.log-prefix:logs-}")
    private String logIndexPrefix;

    private final ElasticsearchOperations esOperations;
    private final ElasticsearchClient     esClient;

    /**
     * 批量写入 ES，索引名按当天日期生成
     */
    public void bulkIndex(List<LogDocument> docs) {
        if (docs.isEmpty()) return;

        String index = logIndexPrefix + LocalDate.now().format(DateTimeFormatter.ofPattern("yyyy.MM.dd"));

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
     * 按条件检索日志（跨天索引查询）
     */
    public List<SearchHit<LogDocument>> search(String terminalId, String keyword,
                                                String startDate, String endDate) {
        Criteria criteria = new Criteria();
        if (terminalId != null) {
            criteria = criteria.and("terminalId").is(terminalId);
        }
        if (keyword != null && !keyword.isBlank()) {
            criteria = criteria.and("message").contains(keyword);
        }

        Query query = new CriteriaQuery(criteria);
        // TODO: 添加时间范围过滤和分页
        return esOperations.search(query, LogDocument.class).getSearchHits();
    }
}
