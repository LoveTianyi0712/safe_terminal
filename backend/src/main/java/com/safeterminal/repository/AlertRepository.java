package com.safeterminal.repository;

import com.safeterminal.domain.entity.Alert;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.Pageable;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;

import java.time.LocalDateTime;
import java.util.List;

public interface AlertRepository extends JpaRepository<Alert, Long> {

    Page<Alert> findByStatusOrderByOccurredAtDesc(Integer status, Pageable pageable);

    Page<Alert> findByTerminalIdOrderByOccurredAtDesc(String terminalId, Pageable pageable);

    long countByStatus(Integer status);

    long countByCreatedAtAfter(LocalDateTime after);

    /** 查询1小时内同一终端的 USB CONNECTED 事件数量 */
    @Query("""
        SELECT COUNT(a) FROM Alert a
        WHERE a.terminalId = :terminalId
          AND a.alertType  = 'USB_CONNECTED'
          AND a.occurredAt > :since
        """)
    long countUsbConnectedSince(String terminalId, LocalDateTime since);

    @Modifying
    @Query("UPDATE Alert a SET a.status = :status, a.operator = :operator, a.resolvedAt = :now WHERE a.id = :id")
    int updateStatus(Long id, Integer status, String operator, LocalDateTime now);
}
