package com.safeterminal.service;

import com.safeterminal.domain.entity.Alert;
import com.safeterminal.proto.TerminalProto;
import com.safeterminal.repository.AlertRepository;
import com.safeterminal.websocket.AlertWebSocketPublisher;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDateTime;
import java.util.List;

/**
 * 规则引擎（简单责任链实现）
 *
 * 规则列表：
 *  1. USB_ABUSE        - 1小时内同一终端插入超过阈值次 U 盘
 *  2. USB_BLACKLIST    - 插入黑名单设备
 *  3. (可扩展) 异常登录规则等
 *
 * 生产可替换为 Drools 规则引擎，将规则持久化到数据库动态加载。
 */
@Slf4j
@Service
@RequiredArgsConstructor
public class AlertRuleEngine {

    private final AlertRepository        alertRepository;
    private final AlertWebSocketPublisher wsPublisher;
    private final PolicyService          policyService;

    @Value("${safe-terminal.alert.usb-threshold-per-hour:3}")
    private int usbThresholdPerHour;

    /**
     * 对每个 USB 事件执行所有规则，命中则生成告警
     */
    @Transactional
    public void evaluate(TerminalProto.UsbEvent event) {
        if (event.getAction() != TerminalProto.UsbAction.USB_CONNECTED) {
            return;
        }

        List<AlertRule> rules = List.of(
            this::checkUsbBlacklist,
            this::checkUsbFrequency
        );

        for (AlertRule rule : rules) {
            var alert = rule.check(event);
            if (alert != null) {
                var saved = alertRepository.save(alert);
                wsPublisher.publishAlert(saved);
                log.info("Alert created: type={} terminal={}", saved.getAlertType(), saved.getTerminalId());
            }
        }
    }

    @FunctionalInterface
    interface AlertRule {
        Alert check(TerminalProto.UsbEvent event);
    }

    private Alert checkUsbBlacklist(TerminalProto.UsbEvent event) {
        var policy = policyService.getPolicyForTerminal(event.getIdentity().getTerminalId());
        if (policy == null) return null;

        boolean blocked = policy.getUsbBlacklist().stream()
                .anyMatch(prefix -> event.getDeviceId().startsWith(prefix));
        if (!blocked) return null;

        return Alert.builder()
                .terminalId(event.getIdentity().getTerminalId())
                .hostname(event.getIdentity().getHostname())
                .alertType("USB_BLACKLIST")
                .severity(3)
                .title("黑名单USB设备接入")
                .description(String.format("终端 %s 接入黑名单设备: %s (%s %s)",
                    event.getIdentity().getHostname(),
                    event.getDeviceId(), event.getVendor(), event.getProduct()))
                .occurredAt(LocalDateTime.now())
                .build();
    }

    private Alert checkUsbFrequency(TerminalProto.UsbEvent event) {
        var terminalId = event.getIdentity().getTerminalId();
        var since      = LocalDateTime.now().minusHours(1);
        long count     = alertRepository.countUsbConnectedSince(terminalId, since);

        if (count < usbThresholdPerHour) return null;

        return Alert.builder()
                .terminalId(terminalId)
                .hostname(event.getIdentity().getHostname())
                .alertType("USB_ABUSE")
                .severity(2)
                .title("USB 频繁插拔告警")
                .description(String.format("终端 %s 过去1小时内USB接入 %d 次，超过阈值 %d",
                    event.getIdentity().getHostname(), count + 1, usbThresholdPerHour))
                .occurredAt(LocalDateTime.now())
                .build();
    }
}
