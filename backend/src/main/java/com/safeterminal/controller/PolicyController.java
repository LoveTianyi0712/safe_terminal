package com.safeterminal.controller;

import com.safeterminal.domain.entity.Policy;
import com.safeterminal.service.PolicyService;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequestMapping("/v1/policies")
@RequiredArgsConstructor
public class PolicyController {

    private final PolicyService policyService;

    @GetMapping
    public List<Policy> list() {
        return policyService.listAll();
    }

    @GetMapping("/{policyId}")
    public Policy get(@PathVariable String policyId) {
        return policyService.get(policyId);
    }

    @PostMapping
    @PreAuthorize("hasAnyRole('ADMIN', 'OPERATOR')")
    public ResponseEntity<Policy> create(@RequestBody Policy policy) {
        return ResponseEntity.status(HttpStatus.CREATED)
                .body(policyService.create(policy));
    }

    @PutMapping("/{policyId}")
    @PreAuthorize("hasAnyRole('ADMIN', 'OPERATOR')")
    public Policy update(@PathVariable String policyId, @RequestBody Policy policy) {
        return policyService.update(policyId, policy);
    }

    @DeleteMapping("/{policyId}")
    @PreAuthorize("hasRole('ADMIN')")
    public ResponseEntity<Void> delete(@PathVariable String policyId) {
        policyService.delete(policyId);
        return ResponseEntity.noContent().build();
    }
}
