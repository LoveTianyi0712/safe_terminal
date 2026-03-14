package com.safeterminal.service;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.safeterminal.domain.entity.Policy;
import com.safeterminal.repository.PolicyRepository;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;
import java.util.UUID;

@Slf4j
@Service
@RequiredArgsConstructor
public class PolicyService {

    private static final String REDIS_POLICY_CHANNEL = "st:policy:updates";

    private final PolicyRepository    policyRepository;
    private final StringRedisTemplate redisTemplate;
    private final ObjectMapper        objectMapper;

    public List<Policy> listAll() {
        return policyRepository.findAll();
    }

    public Policy get(String policyId) {
        return policyRepository.findByPolicyId(policyId)
                .orElseThrow(() -> new RuntimeException("Policy not found: " + policyId));
    }

    @Transactional
    public Policy create(Policy policy) {
        policy.setPolicyId(UUID.randomUUID().toString());
        return policyRepository.save(policy);
    }

    @Transactional
    public Policy update(String policyId, Policy patch) {
        var existing = get(policyId);
        // MapStruct 可替代手动字段赋值
        existing.setName(patch.getName());
        existing.setLogLevel(patch.getLogLevel());
        existing.setHeartbeatIntervalSec(patch.getHeartbeatIntervalSec());
        existing.setUsbBlockEnabled(patch.getUsbBlockEnabled());
        existing.setUsbWhitelist(patch.getUsbWhitelist());
        existing.setUsbBlacklist(patch.getUsbBlacklist());
        var saved = policyRepository.save(existing);

        // 发布策略变更到 Redis，网关订阅后推送给探针
        publishToRedis(saved);
        return saved;
    }

    @Transactional
    public void delete(String policyId) {
        policyRepository.findByPolicyId(policyId).ifPresent(policyRepository::delete);
    }

    public Policy getPolicyForTerminal(String terminalId) {
        // TODO: 查询终端绑定的策略，当前返回默认策略
        return policyRepository.findAll().stream().findFirst().orElse(null);
    }

    private void publishToRedis(Policy policy) {
        try {
            // 将策略序列化为 JSON 发布到 Redis Pub/Sub
            // Go 网关订阅后反序列化为 protobuf Policy 推送给探针
            String json = objectMapper.writeValueAsString(policy);
            redisTemplate.convertAndSend(REDIS_POLICY_CHANNEL, json);
            log.info("Policy published to Redis: {}", policy.getPolicyId());
        } catch (Exception e) {
            log.error("Failed to publish policy to Redis", e);
        }
    }
}
