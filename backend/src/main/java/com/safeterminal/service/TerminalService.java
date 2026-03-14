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
import java.util.List;
import java.util.Map;

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

    /**
     * 批量查询在线状态，一次 Redis HMGET 替代多次单独查询。
     *
     * @param terminalIds 终端 ID 列表
     * @return 每个 ID 对应是否在线的 Map
     */
    public Map<String, Boolean> batchIsOnline(List<String> terminalIds) {
        if (terminalIds == null || terminalIds.isEmpty()) {
            return Map.of();
        }
        // Redis HMGET 一次取出所有值（在线时值为 IP，不在线时为 null）
        List<Object> values = redisTemplate.opsForHash()
                .multiGet(ONLINE_HASH_KEY, new java.util.ArrayList<>(terminalIds));

        Map<String, Boolean> result = new java.util.LinkedHashMap<>(terminalIds.size());
        for (int i = 0; i < terminalIds.size(); i++) {
            result.put(terminalIds.get(i), values.get(i) != null);
        }
        return result;
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
