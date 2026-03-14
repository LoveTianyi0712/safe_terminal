package com.safeterminal.service;

import com.influxdb.client.InfluxDBClient;
import com.influxdb.client.WriteApiBlocking;
import com.influxdb.client.domain.WritePrecision;
import com.influxdb.client.write.Point;
import com.safeterminal.domain.document.UsbEventDocument;
import com.safeterminal.proto.TerminalProto;
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
import java.util.UUID;

@Slf4j
@Service
@RequiredArgsConstructor
public class UsbEventService {

    @Value("${safe-terminal.es.index.usb-prefix:usb-events-}")
    private String usbIndexPrefix;

    @Value("${influxdb.bucket:metrics}")
    private String influxBucket;

    @Value("${influxdb.org:safe-terminal}")
    private String influxOrg;

    private final ElasticsearchOperations esOperations;
    private final InfluxDBClient          influxDBClient;

    public void index(UsbEventDocument doc) {
        String indexName = usbIndexPrefix + LocalDate.now().format(DateTimeFormatter.ofPattern("yyyy.MM.dd"));
        try {
            esOperations.save(doc, org.springframework.data.elasticsearch.core.mapping.IndexCoordinates.of(indexName));
        } catch (Exception e) {
            log.error("Failed to index USB event to ES", e);
        }
    }

    /**
     * USB 事件检索（跨天索引通配查询，支持设备 ID 过滤、时间范围和分页）
     */
    public Page<UsbEventDocument> search(String terminalId, String deviceId,
                                          String startDate, String endDate,
                                          Pageable pageable) {
        Criteria criteria = new Criteria();

        if (terminalId != null && !terminalId.isBlank()) {
            criteria = criteria.and("terminalId").is(terminalId);
        }
        if (deviceId != null && !deviceId.isBlank()) {
            criteria = criteria.and("deviceId").is(deviceId);
        }
        if (startDate != null && !startDate.isBlank() && endDate != null && !endDate.isBlank()) {
            Instant from = Instant.parse(startDate + "T00:00:00.000Z");
            Instant to   = Instant.parse(endDate   + "T23:59:59.999Z");
            criteria = criteria.and("timestamp").between(from, to);
        }

        CriteriaQuery query = new CriteriaQuery(criteria, pageable);

        try {
            var searchPage = esOperations.searchForPage(
                query, UsbEventDocument.class,
                IndexCoordinates.of(usbIndexPrefix + "*"));

            @SuppressWarnings("unchecked")
            Page<UsbEventDocument> result = (Page<UsbEventDocument>) SearchHitSupport.unwrapSearchHits(searchPage);
            return result;
        } catch (Exception e) {
            log.error("USB ES search failed", e);
            return new PageImpl<>(Collections.emptyList(), pageable, 0);
        }
    }

    /**
     * 写入 InfluxDB，用于时序趋势统计（按小时汇总 USB 接入次数）
     */
    public void writeToInflux(TerminalProto.UsbEvent event) {
        try {
            Instant ts = Instant.ofEpochSecond(
                event.getTs().getSeconds(), event.getTs().getNanos());

            Point point = Point.measurement("usb_event")
                .addTag("terminal_id", event.getIdentity().getTerminalId())
                .addTag("hostname",    event.getIdentity().getHostname())
                .addTag("action",      event.getAction().name())
                .addTag("vendor",      event.getVendor())
                .addField("device_id", event.getDeviceId())
                .addField("count", 1L)
                .time(ts, WritePrecision.MS);

            WriteApiBlocking writeApi = influxDBClient.getWriteApiBlocking();
            writeApi.writePoint(influxBucket, influxOrg, point);
        } catch (Exception e) {
            log.error("Failed to write USB event to InfluxDB", e);
        }
    }
}
