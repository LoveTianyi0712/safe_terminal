package com.safeterminal.service;

import com.influxdb.client.InfluxDBClient;
import com.influxdb.client.QueryApi;
import com.influxdb.query.FluxRecord;
import com.influxdb.query.FluxTable;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Service;

import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * 封装 InfluxDB Flux 查询，供 MetricsController 使用。
 *
 * <p>以下方法均使用 Flux 语言查询，结果按时间升序返回。
 * InfluxDB 中的 measurement 约定：
 * <ul>
 *   <li>{@code usb_event}  — 由 {@link UsbEventService#writeToInflux} 写入</li>
 *   <li>{@code heartbeat}  — 由心跳消费者写入（字段：cpu_percent, mem_percent, disk_percent）</li>
 * </ul>
 */
@Slf4j
@Service
@RequiredArgsConstructor
public class InfluxQueryService {

    @Value("${influxdb.bucket:metrics}")
    private String bucket;

    @Value("${influxdb.org:safe-terminal}")
    private String org;

    private final InfluxDBClient influxDBClient;

    /**
     * 查询指定终端在过去 N 小时内每小时的 USB 接入次数。
     *
     * @param terminalId 终端 ID
     * @param hours      查询时间窗口（小时），最大 168（7 天）
     * @return 每个小时桶的统计列表，格式 [{time, count}]
     */
    public List<Map<String, Object>> getUsbTrend(String terminalId, int hours) {
        hours = Math.min(Math.max(hours, 1), 168);
        String flux = String.format("""
                from(bucket: "%s")
                  |> range(start: -%dh)
                  |> filter(fn: (r) => r._measurement == "usb_event")
                  |> filter(fn: (r) => r._field == "count")
                  |> filter(fn: (r) => r.terminal_id == "%s")
                  |> filter(fn: (r) => r.action == "CONNECTED")
                  |> aggregateWindow(every: 1h, fn: sum, createEmpty: true)
                  |> fill(value: 0.0)
                  |> keep(columns: ["_time", "_value"])
                """, bucket, hours, escapeFlux(terminalId));
        return executeQuery(flux, "count");
    }

    /**
     * 查询指定终端在过去 N 分钟内的心跳指标（CPU/内存/磁盘使用率）。
     *
     * @param terminalId 终端 ID
     * @param minutes    查询时间窗口（分钟），最大 1440（24 小时）
     * @return 每条心跳记录，格式 [{time, cpu, mem, disk}]
     */
    public List<Map<String, Object>> getHeartbeatMetrics(String terminalId, int minutes) {
        minutes = Math.min(Math.max(minutes, 1), 1440);
        // 查询三个字段并合并到一行（pivot 操作）
        String flux = String.format("""
                from(bucket: "%s")
                  |> range(start: -%dm)
                  |> filter(fn: (r) => r._measurement == "heartbeat")
                  |> filter(fn: (r) => r.terminal_id == "%s")
                  |> filter(fn: (r) => r._field == "cpu_percent" or r._field == "mem_percent" or r._field == "disk_percent")
                  |> pivot(rowKey: ["_time"], columnKey: ["_field"], valueColumn: "_value")
                  |> keep(columns: ["_time", "cpu_percent", "mem_percent", "disk_percent"])
                  |> sort(columns: ["_time"])
                """, bucket, minutes, escapeFlux(terminalId));
        return executePivotQuery(flux);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /** 通用单值字段查询（_time + _value → [{time, <fieldName>}]） */
    private List<Map<String, Object>> executeQuery(String flux, String fieldName) {
        try {
            QueryApi queryApi = influxDBClient.getQueryApi();
            List<FluxTable> tables = queryApi.query(flux, org);
            List<Map<String, Object>> result = new ArrayList<>();
            for (FluxTable table : tables) {
                for (FluxRecord record : table.getRecords()) {
                    Map<String, Object> row = new LinkedHashMap<>();
                    row.put("time",    record.getTime() != null ? record.getTime().toString() : null);
                    row.put(fieldName, record.getValue());
                    result.add(row);
                }
            }
            return result;
        } catch (Exception e) {
            log.error("InfluxDB query failed: {}", e.getMessage());
            return Collections.emptyList();
        }
    }

    /** pivot 查询（多字段 → 宽表行）*/
    private List<Map<String, Object>> executePivotQuery(String flux) {
        try {
            QueryApi queryApi = influxDBClient.getQueryApi();
            List<FluxTable> tables = queryApi.query(flux, org);
            List<Map<String, Object>> result = new ArrayList<>();
            for (FluxTable table : tables) {
                for (FluxRecord record : table.getRecords()) {
                    Map<String, Object> row = new LinkedHashMap<>();
                    row.put("time",        record.getTime() != null ? record.getTime().toString() : null);
                    row.put("cpu",         record.getValueByKey("cpu_percent"));
                    row.put("mem",         record.getValueByKey("mem_percent"));
                    row.put("disk",        record.getValueByKey("disk_percent"));
                    result.add(row);
                }
            }
            return result;
        } catch (Exception e) {
            log.error("InfluxDB pivot query failed: {}", e.getMessage());
            return Collections.emptyList();
        }
    }

    /** 防止 Flux 注入（简单转义双引号） */
    private static String escapeFlux(String value) {
        return value == null ? "" : value.replace("\"", "\\\"");
    }
}
