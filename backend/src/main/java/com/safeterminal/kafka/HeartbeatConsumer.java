package com.safeterminal.kafka;

import com.google.protobuf.InvalidProtocolBufferException;
import com.safeterminal.proto.TerminalProto;
import com.safeterminal.service.TerminalService;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.apache.kafka.clients.consumer.ConsumerRecord;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.kafka.support.Acknowledgment;
import org.springframework.stereotype.Component;

import java.util.List;

@Slf4j
@Component
@RequiredArgsConstructor
public class HeartbeatConsumer {

    private final TerminalService terminalService;

    @KafkaListener(
        topics     = "${safe-terminal.kafka.topic.heartbeats}",
        groupId    = "st-heartbeat-consumer",
        containerFactory = "batchKafkaListenerContainerFactory"
    )
    public void consumeHeartbeats(List<ConsumerRecord<String, byte[]>> records, Acknowledgment ack) {
        for (ConsumerRecord<String, byte[]> record : records) {
            try {
                var hb = TerminalProto.Heartbeat.parseFrom(record.value());
                // 更新 Redis 在线状态 + 更新 MySQL 终端最后活跃时间
                terminalService.handleHeartbeat(hb);
            } catch (InvalidProtocolBufferException e) {
                log.warn("Failed to parse Heartbeat offset={}", record.offset(), e);
            }
        }
        ack.acknowledge();
    }
}
