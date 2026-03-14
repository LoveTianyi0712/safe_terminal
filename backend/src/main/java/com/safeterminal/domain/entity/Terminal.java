package com.safeterminal.domain.entity;

import jakarta.persistence.*;
import lombok.Data;
import lombok.NoArgsConstructor;

import java.time.LocalDateTime;

@Data
@NoArgsConstructor
@Entity
@Table(name = "terminal")
public class Terminal {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "terminal_id", nullable = false, unique = true, length = 64)
    private String terminalId;

    @Column(nullable = false, length = 255)
    private String hostname;

    @Column(name = "ip_address", length = 64)
    private String ipAddress;

    /** 1=Windows 2=Linux 3=macOS */
    @Column(nullable = false)
    private Integer os;

    @Column(name = "os_version", length = 128)
    private String osVersion;

    @Column(name = "agent_version", length = 32)
    private String agentVersion;

    @Column(length = 128)
    private String department;

    @Column(length = 128)
    private String owner;

    @Column(name = "policy_id", length = 64)
    private String policyId;

    @Column(name = "created_at", nullable = false, updatable = false)
    private LocalDateTime createdAt;

    @Column(name = "updated_at", nullable = false)
    private LocalDateTime updatedAt;

    @PrePersist
    void onCreate() {
        createdAt = updatedAt = LocalDateTime.now();
    }

    @PreUpdate
    void onUpdate() {
        updatedAt = LocalDateTime.now();
    }
}
