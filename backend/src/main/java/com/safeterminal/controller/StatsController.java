package com.safeterminal.controller;

import com.safeterminal.service.StatsService;
import com.safeterminal.service.StatsService.DailyEventStat;
import lombok.RequiredArgsConstructor;
import org.springframework.web.bind.annotation.*;

import java.util.List;
import java.util.Map;

@RestController
@RequestMapping("/v1/stats")
@RequiredArgsConstructor
public class StatsController {

    private final StatsService statsService;

    /**
     * 近 N 天事件趋势（默认 7 天）
     *
     * 返回格式（数组，按日期从早到晚）：
     * [{ "date": "3/8", "logCount": 1200, "usbCount": 45, "alertCount": 8 }, ...]
     */
    @GetMapping("/event-trend")
    public List<DailyEventStat> eventTrend(
            @RequestParam(defaultValue = "7") int days) {
        days = Math.min(Math.max(days, 1), 30);
        return statsService.getEventTrend(days);
    }

    /**
     * 告警类型分布
     *
     * 返回格式（ECharts pie series data）：
     * [{ "name": "USB_ABUSE", "value": 48 }, ...]
     */
    @GetMapping("/alert-distribution")
    public List<Map<String, Object>> alertDistribution() {
        return statsService.getAlertDistribution();
    }
}
