package com.safeterminal.service;

import co.elastic.clients.elasticsearch.ElasticsearchClient;
import co.elastic.clients.elasticsearch.core.CountRequest;
import com.safeterminal.repository.AlertRepository;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Service;

import java.time.LocalDate;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

@Slf4j
@Service
@RequiredArgsConstructor
public class StatsService {

    @Value("${safe-terminal.es.index.log-prefix:logs-}")
    private String logIndexPrefix;

    @Value("${safe-terminal.es.index.usb-prefix:usb-events-}")
    private String usbIndexPrefix;

    private static final DateTimeFormatter DATE_IDX  = DateTimeFormatter.ofPattern("yyyy.MM.dd");
    private static final DateTimeFormatter DATE_LABEL = DateTimeFormatter.ofPattern("M/d");

    private final ElasticsearchClient esClient;
    private final AlertRepository     alertRepository;

    /**
     * 近 N 天事件趋势：返回每天的日志数、USB 事件数、告警数
     */
    public List<DailyEventStat> getEventTrend(int days) {
        List<DailyEventStat> result = new ArrayList<>(days);

        for (int i = days - 1; i >= 0; i--) {
            LocalDate date      = LocalDate.now().minusDays(i);
            String    dateLabel = date.format(DATE_LABEL);
            String    dateIdx   = date.format(DATE_IDX);

            long logCount  = countEsIndex(logIndexPrefix + dateIdx);
            long usbCount  = countEsIndex(usbIndexPrefix + dateIdx);

            // 当天告警数 = [dayStart, nextDayStart) 区间内的告警
            long alertFrom = alertRepository.countByCreatedAtAfter(date.atStartOfDay());
            long alertTo   = alertRepository.countByCreatedAtAfter(date.plusDays(1).atStartOfDay());
            long alertCount = Math.max(alertFrom - alertTo, 0);

            result.add(new DailyEventStat(dateLabel, logCount, usbCount, alertCount));
        }
        return result;
    }

    /**
     * 告警类型分布：按 alert_type 分组计数，返回 {name, value} 列表
     */
    public List<Map<String, Object>> getAlertDistribution() {
        List<Object[]> rows = alertRepository.countByAlertType();
        List<Map<String, Object>> result = new ArrayList<>(rows.size());
        for (Object[] row : rows) {
            Map<String, Object> item = new LinkedHashMap<>();
            item.put("name",  row[0]);
            item.put("value", row[1]);
            result.add(item);
        }
        return result;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private long countEsIndex(String index) {
        try {
            var resp = esClient.count(CountRequest.of(r -> r.index(index)));
            return resp.count();
        } catch (Exception e) {
            // 索引当天未创建（无数据）或 ES 不可达时返回 0，不影响主流程
            log.debug("ES count failed for index {}: {}", index, e.getMessage());
            return 0L;
        }
    }

    // ── inner record ─────────────────────────────────────────────────────────

    public record DailyEventStat(String date, long logCount, long usbCount, long alertCount) {}
}
