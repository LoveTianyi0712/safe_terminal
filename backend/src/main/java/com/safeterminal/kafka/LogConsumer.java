package com.safeterminal.kafka;

import com.google.protobuf.InvalidProtocolBufferException;
import com.safeterminal.domain.document.LogDocument;
import com.safeterminal.proto.TerminalProto;
import com.safeterminal.service.LogIndexService;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.apache.kafka.clients.consumer.ConsumerRecord;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.kafka.support.Acknowledgment;
import org.springframework.stereotype.Component;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;

@Slf4j
@Component
@RequiredArgsConstructor
public class LogConsumer {

    private final LogIndexService logIndexService;

    /**
     * 批量消费日志 Topic，写入 Elasticsearch
     * containerFactory 使用批量模式，每次最多拉取 500 条
     */
    @KafkaListener(
        topics     = "${safe-terminal.kafka.topic.logs}",
        groupId    = "st-log-consumer",
        containerFactory = "batchKafkaListenerContainerFactory"
    )
    public void consumeLogs(List<ConsumerRecord<String, byte[]>> records, Acknowledgment ack) {
        List<LogDocument> docs = new ArrayList<>(records.size());

        for (ConsumerRecord<String, byte[]> record : records) {
            try {
                var entry = TerminalProto.LogEntry.parseFrom(record.value());
                docs.add(toDocument(entry));
            } catch (InvalidProtocolBufferException e) {
                log.warn("Failed to parse LogEntry from partition={} offset={}",
                         record.partition(), record.offset(), e);
            }
        }

        if (!docs.isEmpty()) {
            logIndexService.bulkIndex(docs);
        }
        ack.acknowledge();
    }

    private LogDocument toDocument(TerminalProto.LogEntry entry) {
        var id = entry.getIdentity();
        return LogDocument.builder()
                .terminalId(id.getTerminalId())
                .hostname(id.getHostname())
                .ipAddress(id.getIpAddress())
                .os(id.getOsValue())
                .timestamp(Instant.ofEpochSecond(
                    entry.getTs().getSeconds(), entry.getTs().getNanos()))
                .severity(entry.getSeverityValue())
                .source(entry.getSource())
                .eventId(entry.getEventId())
                .message(entry.getMessage())
                .extra(entry.getExtraMap())
                .build();
    }
}
