package com.safeterminal.websocket;

import com.safeterminal.domain.entity.Alert;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.messaging.simp.SimpMessagingTemplate;
import org.springframework.stereotype.Component;

/**
 * 通过 STOMP WebSocket 推送实时告警到前端
 * 前端订阅 /topic/alerts 即可接收
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class AlertWebSocketPublisher {

    private final SimpMessagingTemplate messagingTemplate;

    public void publishAlert(Alert alert) {
        try {
            messagingTemplate.convertAndSend("/topic/alerts", alert);
            log.debug("Alert pushed via WebSocket: id={}", alert.getId());
        } catch (Exception e) {
            log.error("Failed to push alert via WebSocket", e);
        }
    }
}
