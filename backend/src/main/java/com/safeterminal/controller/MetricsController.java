package com.safeterminal.controller;

import com.safeterminal.service.InfluxQueryService;
import lombok.RequiredArgsConstructor;
import org.springframework.web.bind.annotation.*;

import java.util.List;
import java.util.Map;

@RestController
@RequestMapping("/v1/metrics")
@RequiredArgsConstructor
public class MetricsController {

    private final InfluxQueryService influxQueryService;

    /**
     * 查询指定终端的 USB 接入趋势（按小时分桶）
     *
     * @param terminalId 终端 ID
     * @param hours      查询时间窗口（小时），默认 24，最大 168（7天）
     *
     * 返回示例：
     * [{ "time": "2026-03-14T08:00:00Z", "count": 3 }, ...]
     */
    @GetMapping("/usb-trend")
    public List<Map<String, Object>> usbTrend(
            @RequestParam String terminalId,
            @RequestParam(defaultValue = "24") int hours) {
        return influxQueryService.getUsbTrend(terminalId, hours);
    }

    /**
     * 查询指定终端的心跳指标（CPU / 内存 / 磁盘使用率）
     *
     * @param terminalId 终端 ID
     * @param minutes    查询时间窗口（分钟），默认 60，最大 1440（24小时）
     *
     * 返回示例：
     * [{ "time": "2026-03-14T10:30:00Z", "cpu": 12.5, "mem": 45.2, "disk": 67.8 }, ...]
     */
    @GetMapping("/heartbeat")
    public List<Map<String, Object>> heartbeat(
            @RequestParam String terminalId,
            @RequestParam(defaultValue = "60") int minutes) {
        return influxQueryService.getHeartbeatMetrics(terminalId, minutes);
    }
}
