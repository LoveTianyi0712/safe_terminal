package com.safeterminal.domain.entity;

import jakarta.persistence.*;
import lombok.Data;
import lombok.NoArgsConstructor;
import org.hibernate.annotations.JdbcTypeCode;
import org.hibernate.type.SqlTypes;

import java.time.LocalDateTime;
import java.util.List;

@Data
@NoArgsConstructor
@Entity
@Table(name = "policy")
public class Policy {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "policy_id", nullable = false, unique = true, length = 64)
    private String policyId;

    @Column(nullable = false, length = 128)
    private String name;

    @Column(columnDefinition = "TEXT")
    private String description;

    /** 0=INFO 1=WARNING 2=ERROR 3=CRITICAL */
    @Column(name = "log_level", nullable = false)
    private Integer logLevel = 1;

    @Column(name = "heartbeat_interval_sec", nullable = false)
    private Integer heartbeatIntervalSec = 30;

    @Column(name = "usb_block_enabled", nullable = false)
    private Boolean usbBlockEnabled = false;

    @JdbcTypeCode(SqlTypes.JSON)
    @Column(name = "usb_whitelist", columnDefinition = "JSON")
    private List<String> usbWhitelist;

    @JdbcTypeCode(SqlTypes.JSON)
    @Column(name = "usb_blacklist", columnDefinition = "JSON")
    private List<String> usbBlacklist;

    @Column(name = "usb_alert_threshold", nullable = false)
    private Integer usbAlertThreshold = 3;

    @Column(name = "created_by", length = 64)
    private String createdBy;

    @Column(name = "created_at", nullable = false, updatable = false)
    private LocalDateTime createdAt;

    @Column(name = "updated_at", nullable = false)
    private LocalDateTime updatedAt;

    @PrePersist
    void onCreate() { createdAt = updatedAt = LocalDateTime.now(); }

    @PreUpdate
    void onUpdate() { updatedAt = LocalDateTime.now(); }
}
