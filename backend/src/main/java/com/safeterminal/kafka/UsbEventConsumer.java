package com.safeterminal.kafka;

import com.google.protobuf.InvalidProtocolBufferException;
import com.safeterminal.domain.document.UsbEventDocument;
import com.safeterminal.proto.TerminalProto;
import com.safeterminal.service.AlertRuleEngine;
import com.safeterminal.service.UsbEventService;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.apache.kafka.clients.consumer.ConsumerRecord;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.kafka.support.Acknowledgment;
import org.springframework.stereotype.Component;

import java.time.Instant;
import java.util.List;

@Slf4j
@Component
@RequiredArgsConstructor
public class UsbEventConsumer {

    private final UsbEventService usbEventService;
    private final AlertRuleEngine alertRuleEngine;

    @KafkaListener(
        topics     = "${safe-terminal.kafka.topic.usb}",
        groupId    = "st-usb-consumer",
        containerFactory = "batchKafkaListenerContainerFactory"
    )
    public void consumeUsbEvents(List<ConsumerRecord<String, byte[]>> records, Acknowledgment ack) {
        for (ConsumerRecord<String, byte[]> record : records) {
            try {
                var event = TerminalProto.UsbEvent.parseFrom(record.value());
                var doc   = toDocument(event);

                // 写入 ES
                usbEventService.index(doc);

                // 写入 InfluxDB（时序趋势分析）
                usbEventService.writeToInflux(event);

                // 规则引擎评估：生成告警
                alertRuleEngine.evaluate(event);

            } catch (InvalidProtocolBufferException e) {
                log.warn("Failed to parse UsbEvent offset={}", record.offset(), e);
            }
        }
        ack.acknowledge();
    }

    private UsbEventDocument toDocument(TerminalProto.UsbEvent event) {
        var id = event.getIdentity();
        return UsbEventDocument.builder()
                .terminalId(id.getTerminalId())
                .hostname(id.getHostname())
                .timestamp(Instant.ofEpochSecond(
                    event.getTs().getSeconds(), event.getTs().getNanos()))
                .action(event.getActionValue())
                .deviceId(event.getDeviceId())
                .vendor(event.getVendor())
                .product(event.getProduct())
                .serialNumber(event.getSerialNumber())
                .mountPoint(event.getMountPoint())
                .build();
    }
}
