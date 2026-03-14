package com.safeterminal.service;

import com.safeterminal.domain.entity.Terminal;
import com.safeterminal.proto.TerminalProto;
import com.safeterminal.repository.TerminalRepository;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.Pageable;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.Duration;
import java.time.LocalDateTime;

@Slf4j
@Service
@RequiredArgsConstructor
public class TerminalService {

    private static final String ONLINE_HASH_KEY = "st:online:terminals";
    private static final Duration ONLINE_TTL    = Duration.ofSeconds(90);

    private final TerminalRepository    terminalRepository;
    private final StringRedisTemplate   redisTemplate;

    @Transactional
    public void handleHeartbeat(TerminalProto.Heartbeat hb) {
        var id = hb.getIdentity();

        // 更新 Redis 在线状态
        redisTemplate.opsForHash().put(ONLINE_HASH_KEY, id.getTerminalId(), id.getIpAddress());
        redisTemplate.expire(ONLINE_HASH_KEY + ":" + id.getTerminalId(), ONLINE_TTL);

        // 首次心跳时注册终端
        if (!terminalRepository.existsByTerminalId(id.getTerminalId())) {
            var terminal = new Terminal();
            terminal.setTerminalId(id.getTerminalId());
            terminal.setHostname(id.getHostname());
            terminal.setIpAddress(id.getIpAddress());
            terminal.setOs(id.getOsValue());
            terminal.setOsVersion(id.getOsVersion());
            terminal.setAgentVersion(id.getAgentVersion());
            terminalRepository.save(terminal);
            log.info("New terminal registered: {}", id.getTerminalId());
        } else {
            // 更新最后在线时间和 IP
            terminalRepository.findByTerminalId(id.getTerminalId()).ifPresent(t -> {
                t.setIpAddress(id.getIpAddress());
                t.setAgentVersion(id.getAgentVersion());
                terminalRepository.save(t);
            });
        }
    }

    public boolean isOnline(String terminalId) {
        return Boolean.TRUE.equals(
            redisTemplate.opsForHash().hasKey(ONLINE_HASH_KEY, terminalId));
    }

    public long countOnline() {
        Long size = redisTemplate.opsForHash().size(ONLINE_HASH_KEY);
        return size != null ? size : 0;
    }

    public Page<Terminal> listTerminals(String keyword, Pageable pageable) {
        if (keyword == null || keyword.isBlank()) {
            return terminalRepository.findAll(pageable);
        }
        return terminalRepository.searchByKeyword(keyword, pageable);
    }

    public Terminal getTerminal(String terminalId) {
        return terminalRepository.findByTerminalId(terminalId)
                .orElseThrow(() -> new RuntimeException("Terminal not found: " + terminalId));
    }

    @Transactional
    public void updateTerminal(String terminalId, String department, String owner) {
        terminalRepository.findByTerminalId(terminalId).ifPresent(t -> {
            t.setDepartment(department);
            t.setOwner(owner);
            terminalRepository.save(t);
        });
    }
}
