package com.safeterminal.security;

import io.jsonwebtoken.*;
import io.jsonwebtoken.io.Decoders;
import io.jsonwebtoken.security.Keys;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.security.core.Authentication;
import org.springframework.security.core.GrantedAuthority;
import org.springframework.stereotype.Component;

import javax.crypto.SecretKey;
import java.time.Instant;
import java.util.Date;
import java.util.stream.Collectors;

/**
 * JWT 生成、解析、校验工具
 *
 * Token Payload 结构:
 * {
 *   "sub":  "admin",           // 用户名
 *   "role": "ADMIN",           // 用户角色
 *   "iat":  1720000000,        // 签发时间
 *   "exp":  1720086400         // 过期时间
 * }
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class JwtTokenProvider {

    private final JwtProperties jwtProperties;

    /** 生成 Access Token */
    public String generateToken(Authentication auth) {
        String roles = auth.getAuthorities().stream()
                .map(GrantedAuthority::getAuthority)
                .collect(Collectors.joining(","));

        Instant now    = Instant.now();
        Instant expiry = now.plusMillis(jwtProperties.getExpirationMs());

        return Jwts.builder()
                .subject(auth.getName())
                .claim("role", roles)
                .issuedAt(Date.from(now))
                .expiration(Date.from(expiry))
                .signWith(getSigningKey())
                .compact();
    }

    /** 生成 Refresh Token（payload 仅含 sub） */
    public String generateRefreshToken(String username) {
        Instant now    = Instant.now();
        Instant expiry = now.plusMillis(jwtProperties.getRefreshExpirationMs());

        return Jwts.builder()
                .subject(username)
                .issuedAt(Date.from(now))
                .expiration(Date.from(expiry))
                .signWith(getSigningKey())
                .compact();
    }

    /** 从 Token 提取用户名 */
    public String extractUsername(String token) {
        return parseClaims(token).getSubject();
    }

    /** 从 Token 提取角色字符串 */
    public String extractRole(String token) {
        return parseClaims(token).get("role", String.class);
    }

    /** 从 Token 提取过期时间 */
    public Date extractExpiration(String token) {
        return parseClaims(token).getExpiration();
    }

    /**
     * 校验 Token 是否有效
     *
     * @return true = 有效；抛出异常 = 无效（由调用方处理）
     */
    public boolean validateToken(String token) {
        try {
            parseClaims(token);
            return true;
        } catch (ExpiredJwtException e) {
            log.warn("JWT token expired: {}", e.getMessage());
        } catch (UnsupportedJwtException e) {
            log.warn("JWT token unsupported: {}", e.getMessage());
        } catch (MalformedJwtException e) {
            log.warn("JWT token malformed: {}", e.getMessage());
        } catch (SecurityException e) {
            log.warn("JWT signature invalid: {}", e.getMessage());
        } catch (IllegalArgumentException e) {
            log.warn("JWT claims empty: {}", e.getMessage());
        }
        return false;
    }

    // ─── 内部辅助 ─────────────────────────────────────────────────────────

    private Claims parseClaims(String token) {
        return Jwts.parser()
                .verifyWith(getSigningKey())
                .build()
                .parseSignedClaims(token)
                .getPayload();
    }

    private SecretKey getSigningKey() {
        byte[] keyBytes = Decoders.BASE64.decode(jwtProperties.getSecret());
        return Keys.hmacShaKeyFor(keyBytes);
    }
}
