package com.safeterminal.config;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import java.time.LocalDate;
import java.time.format.DateTimeFormatter;

/**
 * ES 动态索引名解析器
 *
 * <p>LogDocument / UsbEventDocument 通过 SpEL 引用此 Bean：
 * {@code @Document(indexName = "#{@indexNameResolver.resolveLogIndex()}")}
 *
 * <p>索引按天分割，示例：logs-2025.04.07、usb-events-2025.04.07
 */
@Component("indexNameResolver")
public class EsIndexNameResolver {

    private static final DateTimeFormatter DATE_FMT = DateTimeFormatter.ofPattern("yyyy.MM.dd");

    @Value("${safe-terminal.es.index.log-prefix:logs-}")
    private String logPrefix;

    @Value("${safe-terminal.es.index.usb-prefix:usb-events-}")
    private String usbPrefix;

    /** 当天日志索引，例如 logs-2025.04.07 */
    public String resolveLogIndex() {
        return logPrefix + LocalDate.now().format(DATE_FMT);
    }

    /** 当天 USB 事件索引，例如 usb-events-2025.04.07 */
    public String resolveUsbIndex() {
        return usbPrefix + LocalDate.now().format(DATE_FMT);
    }
}
