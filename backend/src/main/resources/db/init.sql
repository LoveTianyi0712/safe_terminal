-- 终端安全监控系统 MySQL 初始化脚本
-- 字符集: utf8mb4

CREATE DATABASE IF NOT EXISTS safe_terminal CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE safe_terminal;

-- ─── 终端元数据 ────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS terminal (
  id            BIGINT       NOT NULL AUTO_INCREMENT,
  terminal_id   VARCHAR(64)  NOT NULL UNIQUE COMMENT 'UUID，与探针一致',
  hostname      VARCHAR(255) NOT NULL,
  ip_address    VARCHAR(64),
  os            TINYINT      NOT NULL COMMENT '1=Windows 2=Linux 3=macOS',
  os_version    VARCHAR(128),
  agent_version VARCHAR(32),
  department    VARCHAR(128) COMMENT '归属部门',
  owner         VARCHAR(128) COMMENT '责任人',
  policy_id     VARCHAR(64)  COMMENT '当前应用的策略ID',
  created_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  INDEX idx_terminal_id (terminal_id),
  INDEX idx_department (department)
) ENGINE=InnoDB COMMENT='终端元数据';

-- ─── 用户 ──────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS sys_user (
  id         BIGINT       NOT NULL AUTO_INCREMENT,
  username   VARCHAR(64)  NOT NULL UNIQUE,
  password   VARCHAR(128) NOT NULL COMMENT 'BCrypt',
  role       VARCHAR(32)  NOT NULL DEFAULT 'VIEWER' COMMENT 'ADMIN/OPERATOR/VIEWER',
  email      VARCHAR(128),
  enabled    TINYINT(1)   NOT NULL DEFAULT 1,
  created_at DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id)
) ENGINE=InnoDB COMMENT='系统用户';

INSERT IGNORE INTO sys_user (username, password, role)
VALUES ('admin', '$2a$10$X5GlJzSCh.Y9O5jrpEZ1u.wQ7RVSqkEy.E1RaO1mDqSV6Fxyk5v6y', 'ADMIN');
-- 默认密码: Admin@123456 (BCrypt)

-- ─── 监控策略 ──────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS policy (
  id                       BIGINT       NOT NULL AUTO_INCREMENT,
  policy_id                VARCHAR(64)  NOT NULL UNIQUE,
  name                     VARCHAR(128) NOT NULL,
  description              TEXT,
  log_level                TINYINT      NOT NULL DEFAULT 1 COMMENT '0=INFO 1=WARNING 2=ERROR',
  heartbeat_interval_sec   INT          NOT NULL DEFAULT 30,
  usb_block_enabled        TINYINT(1)   NOT NULL DEFAULT 0,
  usb_whitelist            JSON         COMMENT '["VID_1234&PID_5678"]',
  usb_blacklist            JSON,
  usb_alert_threshold      INT          NOT NULL DEFAULT 3 COMMENT '1h内插入次数告警阈值',
  created_by               VARCHAR(64),
  created_at               DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at               DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  INDEX idx_policy_id (policy_id)
) ENGINE=InnoDB COMMENT='监控策略';

-- ─── 告警记录 ──────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS alert (
  id           BIGINT        NOT NULL AUTO_INCREMENT,
  terminal_id  VARCHAR(64)   NOT NULL,
  hostname     VARCHAR(255),
  alert_type   VARCHAR(64)   NOT NULL COMMENT 'USB_ABUSE/SUSPICIOUS_LOGIN/...',
  severity     TINYINT       NOT NULL DEFAULT 1,
  title        VARCHAR(255)  NOT NULL,
  description  TEXT,
  raw_event_id VARCHAR(128)  COMMENT 'ES 文档 ID（关联原始事件）',
  status       TINYINT       NOT NULL DEFAULT 0 COMMENT '0=OPEN 1=CONFIRMED 2=RESOLVED',
  operator     VARCHAR(64)   COMMENT '处理人',
  comment      TEXT          COMMENT '处理备注',
  occurred_at  DATETIME      NOT NULL,
  created_at   DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  resolved_at  DATETIME,
  PRIMARY KEY (id),
  INDEX idx_terminal_id (terminal_id),
  INDEX idx_status (status),
  INDEX idx_occurred_at (occurred_at)
) ENGINE=InnoDB COMMENT='告警记录';
