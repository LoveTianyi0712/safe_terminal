package com.safeterminal.controller;

import com.safeterminal.security.JwtProperties;
import com.safeterminal.security.JwtTokenProvider;
import com.safeterminal.security.UserDetailsServiceImpl;
import jakarta.validation.Valid;
import jakarta.validation.constraints.NotBlank;
import lombok.Data;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.authentication.AuthenticationManager;
import org.springframework.security.authentication.BadCredentialsException;
import org.springframework.security.authentication.DisabledException;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.Authentication;
import org.springframework.security.core.GrantedAuthority;
import org.springframework.web.bind.annotation.*;

import java.time.Instant;
import java.util.Map;

@Slf4j
@RestController
@RequestMapping("/auth")
@RequiredArgsConstructor
public class AuthController {

    private final AuthenticationManager  authManager;
    private final JwtTokenProvider       jwtTokenProvider;
    private final UserDetailsServiceImpl userDetailsService;
    private final JwtProperties          jwtProperties;

    // ─── Request / Response DTO ──────────────────────────────────────────

    @Data
    public static class LoginRequest {
        @NotBlank(message = "用户名不能为空")
        private String username;

        @NotBlank(message = "密码不能为空")
        private String password;
    }

    @Data
    public static class LoginResponse {
        private final String token;
        private final String refreshToken;
        private final long   expiresAt;     // Unix 时间戳（秒）
        private final String username;
        private final String role;
    }

    @Data
    public static class RefreshRequest {
        @NotBlank
        private String refreshToken;
    }

    // ─── 登录接口 ─────────────────────────────────────────────────────────

    /**
     * POST /auth/login
     * 返回 Access Token（24h）和 Refresh Token（7d）
     */
    @PostMapping("/login")
    public ResponseEntity<?> login(@Valid @RequestBody LoginRequest req) {
        try {
            // 1. 交给 AuthenticationManager 做用户名/密码校验
            Authentication auth = authManager.authenticate(
                new UsernamePasswordAuthenticationToken(req.getUsername(), req.getPassword())
            );

            // 2. 生成 Token
            String accessToken  = jwtTokenProvider.generateToken(auth);
            String refreshToken = jwtTokenProvider.generateRefreshToken(auth.getName());

            // 3. 提取角色（去掉 ROLE_ 前缀返回给前端）
            String role = auth.getAuthorities().stream()
                    .map(GrantedAuthority::getAuthority)
                    .map(a -> a.replace("ROLE_", ""))
                    .findFirst()
                    .orElse("VIEWER");

            long expiresAt = Instant.now()
                    .plusMillis(jwtProperties.getExpirationMs())
                    .getEpochSecond();

            log.info("User login success: {}, role={}", auth.getName(), role);

            return ResponseEntity.ok(new LoginResponse(
                accessToken, refreshToken, expiresAt, auth.getName(), role
            ));

        } catch (BadCredentialsException e) {
            log.warn("Login failed for user: {}", req.getUsername());
            return ResponseEntity.status(HttpStatus.UNAUTHORIZED)
                    .body(Map.of("code", 401, "message", "用户名或密码错误"));
        } catch (DisabledException e) {
            return ResponseEntity.status(HttpStatus.FORBIDDEN)
                    .body(Map.of("code", 403, "message", "账号已被禁用"));
        }
    }

    // ─── 刷新 Token 接口 ──────────────────────────────────────────────────

    /**
     * POST /auth/refresh
     * 用 Refresh Token 换取新的 Access Token，无需重新登录
     */
    @PostMapping("/refresh")
    public ResponseEntity<?> refresh(@Valid @RequestBody RefreshRequest req) {
        String refreshToken = req.getRefreshToken();

        if (!jwtTokenProvider.validateToken(refreshToken)) {
            return ResponseEntity.status(HttpStatus.UNAUTHORIZED)
                    .body(Map.of("code", 401, "message", "Refresh Token 无效或已过期，请重新登录"));
        }

        String username    = jwtTokenProvider.extractUsername(refreshToken);
        var    userDetails = userDetailsService.loadUserByUsername(username);

        // 重新构造 Authentication 对象以生成新 Access Token
        var auth = new UsernamePasswordAuthenticationToken(
                userDetails, null, userDetails.getAuthorities());

        String newAccessToken = jwtTokenProvider.generateToken(auth);
        long   expiresAt      = Instant.now()
                .plusMillis(jwtProperties.getExpirationMs())
                .getEpochSecond();

        String role = userDetails.getAuthorities().stream()
                .map(GrantedAuthority::getAuthority)
                .map(a -> a.replace("ROLE_", ""))
                .findFirst()
                .orElse("VIEWER");

        log.info("Token refreshed for user: {}", username);

        return ResponseEntity.ok(new LoginResponse(
            newAccessToken, refreshToken, expiresAt, username, role
        ));
    }

    // ─── 当前用户信息 ─────────────────────────────────────────────────────

    /**
     * GET /auth/me
     * 返回当前登录用户信息（需携带有效 Token）
     */
    @GetMapping("/me")
    public ResponseEntity<?> me(Authentication auth) {
        if (auth == null) {
            return ResponseEntity.status(HttpStatus.UNAUTHORIZED).build();
        }
        String role = auth.getAuthorities().stream()
                .map(GrantedAuthority::getAuthority)
                .map(a -> a.replace("ROLE_", ""))
                .findFirst()
                .orElse("VIEWER");
        return ResponseEntity.ok(Map.of(
            "username", auth.getName(),
            "role",     role
        ));
    }
}
