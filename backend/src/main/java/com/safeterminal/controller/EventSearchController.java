package com.safeterminal.controller;

import com.safeterminal.domain.document.LogDocument;
import com.safeterminal.service.LogIndexService;
import lombok.RequiredArgsConstructor;
import org.springframework.data.elasticsearch.core.SearchHit;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequestMapping("/v1/events")
@RequiredArgsConstructor
public class EventSearchController {

    private final LogIndexService logIndexService;

    /**
     * 日志全文检索
     *
     * @param terminalId 按终端过滤（可选）
     * @param keyword    关键词（ES 全文检索）
     * @param startDate  开始日期 yyyy-MM-dd（可选）
     * @param endDate    结束日期 yyyy-MM-dd（可选）
     */
    @GetMapping("/logs")
    public List<LogDocument> searchLogs(
            @RequestParam(required = false) String terminalId,
            @RequestParam(required = false) String keyword,
            @RequestParam(required = false) String startDate,
            @RequestParam(required = false) String endDate) {
        return logIndexService.search(terminalId, keyword, startDate, endDate)
                .stream()
                .map(SearchHit::getContent)
                .toList();
    }

    // TODO: /v1/events/usb  - USB 事件检索（类似 searchLogs 实现）
}
