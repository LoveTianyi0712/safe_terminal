package com.safeterminal.controller;

import com.safeterminal.domain.document.LogDocument;
import com.safeterminal.domain.document.UsbEventDocument;
import com.safeterminal.service.LogIndexService;
import com.safeterminal.service.UsbEventService;
import lombok.RequiredArgsConstructor;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.PageRequest;
import org.springframework.data.domain.Sort;
import org.springframework.web.bind.annotation.*;

@RestController
@RequestMapping("/v1/events")
@RequiredArgsConstructor
public class EventSearchController {

    private final LogIndexService  logIndexService;
    private final UsbEventService  usbEventService;

    /**
     * 日志全文检索（跨天索引，支持时间范围过滤和分页）
     *
     * @param terminalId 按终端 ID 过滤（可选）
     * @param keyword    消息关键词，走 ES match 全文检索（可选）
     * @param startDate  开始日期 yyyy-MM-dd（可选）
     * @param endDate    结束日期 yyyy-MM-dd（可选）
     * @param page       页码，从 0 开始，默认 0
     * @param size       每页条数，默认 20，上限 200
     */
    @GetMapping("/logs")
    public Page<LogDocument> searchLogs(
            @RequestParam(required = false) String terminalId,
            @RequestParam(required = false) String keyword,
            @RequestParam(required = false) String startDate,
            @RequestParam(required = false) String endDate,
            @RequestParam(defaultValue = "0")  int page,
            @RequestParam(defaultValue = "20") int size) {
        size = Math.min(size, 200);
        var pageable = PageRequest.of(page, size, Sort.by(Sort.Direction.DESC, "timestamp"));
        return logIndexService.search(terminalId, keyword, startDate, endDate, pageable);
    }

    /**
     * USB 事件检索（跨天索引，支持时间范围过滤和分页）
     *
     * @param terminalId 按终端 ID 过滤（可选）
     * @param deviceId   按设备 ID 过滤，如 VID_0951&PID_1666（可选）
     * @param startDate  开始日期 yyyy-MM-dd（可选）
     * @param endDate    结束日期 yyyy-MM-dd（可选）
     * @param page       页码，从 0 开始
     * @param size       每页条数，默认 20，上限 200
     */
    @GetMapping("/usb")
    public Page<UsbEventDocument> searchUsb(
            @RequestParam(required = false) String terminalId,
            @RequestParam(required = false) String deviceId,
            @RequestParam(required = false) String startDate,
            @RequestParam(required = false) String endDate,
            @RequestParam(defaultValue = "0")  int page,
            @RequestParam(defaultValue = "20") int size) {
        size = Math.min(size, 200);
        var pageable = PageRequest.of(page, size, Sort.by(Sort.Direction.DESC, "timestamp"));
        return usbEventService.search(terminalId, deviceId, startDate, endDate, pageable);
    }
}
