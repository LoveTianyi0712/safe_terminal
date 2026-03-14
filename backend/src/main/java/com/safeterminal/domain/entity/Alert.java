package com.safeterminal.domain.entity;

import jakarta.persistence.*;
import lombok.Builder;
import lombok.Data;
import lombok.NoArgsConstructor;
import lombok.AllArgsConstructor;

import java.time.LocalDateTime;

@Data
@Builder
@NoArgsConstructor
@AllArgsConstructor
@Entity
@Table(name = "alert",
       indexes = {
           @Index(name = "idx_terminal_id", columnList = "terminal_id"),
           @Index(name = "idx_status",      columnList = "status"),
           @Index(name = "idx_occurred_at", columnList = "occurred_at")
       })
public class Alert {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "terminal_id", nullable = false, length = 64)
    private String terminalId;

    @Column(length = 255)
    private String hostname;

    @Column(name = "alert_type", nullable = false, length = 64)
    private String alertType;

    /** 0=INFO 1=WARNING 2=ERROR 3=CRITICAL */
    @Column(nullable = false)
    private Integer severity;

    @Column(nullable = false, length = 255)
    private String title;

    @Column(columnDefinition = "TEXT")
    private String description;

    @Column(name = "raw_event_id", length = 128)
    private String rawEventId;

    /** 0=OPEN 1=CONFIRMED 2=RESOLVED */
    @Column(nullable = false)
    @Builder.Default
    private Integer status = 0;

    @Column(length = 64)
    private String operator;

    @Column(columnDefinition = "TEXT")
    private String comment;

    @Column(name = "occurred_at", nullable = false)
    private LocalDateTime occurredAt;

    @Column(name = "created_at", nullable = false, updatable = false)
    private LocalDateTime createdAt;

    @Column(name = "resolved_at")
    private LocalDateTime resolvedAt;

    @PrePersist
    void onCreate() {
        createdAt = LocalDateTime.now();
    }
}
