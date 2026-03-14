package com.safeterminal.repository;

import com.safeterminal.domain.entity.Terminal;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.Pageable;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;

import java.util.Optional;

public interface TerminalRepository extends JpaRepository<Terminal, Long> {

    Optional<Terminal> findByTerminalId(String terminalId);

    boolean existsByTerminalId(String terminalId);

    Page<Terminal> findByDepartmentContainingIgnoreCase(String department, Pageable pageable);

    @Query("SELECT t FROM Terminal t WHERE t.hostname LIKE %:keyword% OR t.ipAddress LIKE %:keyword%")
    Page<Terminal> searchByKeyword(String keyword, Pageable pageable);
}
