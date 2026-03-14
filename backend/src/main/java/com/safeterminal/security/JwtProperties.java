package com.safeterminal.security;

import lombok.Data;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.stereotype.Component;

@Data
@Component
@ConfigurationProperties(prefix = "jwt")
public class JwtProperties {

    /** Base64 编码的 HMAC-SHA256 密钥（至少 256 bit） */
    private String secret;

    /** Token 有效期（毫秒），默认 24 小时 */
    private long expirationMs = 86_400_000L;

    /** Refresh Token 有效期（毫秒），默认 7 天 */
    private long refreshExpirationMs = 604_800_000L;
}
