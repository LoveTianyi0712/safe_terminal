package com.safeterminal.controller;

import com.safeterminal.domain.entity.Alert;
import com.safeterminal.repository.AlertRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.PageRequest;
import org.springframework.data.domain.Sort;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.*;

import java.time.LocalDateTime;
import java.util.Map;

@RestController
@RequestMapping("/v1/alerts")
@RequiredArgsConstructor
public class AlertController {

    private final AlertRepository alertRepository;

    @GetMapping
    public Page<Alert> list(
            @RequestParam(required = false) Integer status,
            @RequestParam(required = false) String terminalId,
            @RequestParam(defaultValue = "0") int page,
            @RequestParam(defaultValue = "20") int size) {
        var pageable = PageRequest.of(page, size, Sort.by("occurredAt").descending());
        if (terminalId != null) {
            return alertRepository.findByTerminalIdOrderByOccurredAtDesc(terminalId, pageable);
        }
        if (status != null) {
            return alertRepository.findByStatusOrderByOccurredAtDesc(status, pageable);
        }
        return alertRepository.findAll(pageable);
    }

    @GetMapping("/stats")
    public Map<String, Long> stats() {
        return Map.of(
            "open",     alertRepository.countByStatus(0),
            "today",    alertRepository.countByCreatedAtAfter(LocalDateTime.now().toLocalDate().atStartOfDay())
        );
    }

    @PatchMapping("/{id}/confirm")
    public ResponseEntity<Void> confirm(@PathVariable Long id, Authentication auth) {
        alertRepository.updateStatus(id, 1, auth.getName(), LocalDateTime.now());
        return ResponseEntity.ok().build();
    }

    @PatchMapping("/{id}/resolve")
    @org.springframework.transaction.annotation.Transactional
    public ResponseEntity<Void> resolve(
            @PathVariable Long id,
            @RequestBody Map<String, String> body,
            Authentication auth) {
        alertRepository.updateStatus(id, 2, auth.getName(), LocalDateTime.now());
        String comment = body.get("comment");
        if (comment != null && !comment.isBlank()) {
            alertRepository.updateComment(id, comment);
        }
        return ResponseEntity.ok().build();
    }
}
