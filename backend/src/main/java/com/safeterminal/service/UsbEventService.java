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
import org.springframework.data.elasticsearch.core.ElasticsearchOperations;
import org.springframework.stereotype.Service;

import java.time.Instant;
import java.time.LocalDate;
import java.time.format.DateTimeFormatter;
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
