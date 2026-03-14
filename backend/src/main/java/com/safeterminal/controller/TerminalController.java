package com.safeterminal.controller;

import com.safeterminal.domain.entity.Terminal;
import com.safeterminal.service.TerminalService;
import lombok.RequiredArgsConstructor;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.PageRequest;
import org.springframework.data.domain.Sort;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.Arrays;
import java.util.List;
import java.util.Map;

@RestController
@RequestMapping("/v1/terminals")
@RequiredArgsConstructor
public class TerminalController {

    private final TerminalService terminalService;

    @GetMapping
    public Page<Terminal> list(
            @RequestParam(required = false) String keyword,
            @RequestParam(defaultValue = "0") int page,
            @RequestParam(defaultValue = "20") int size) {
        var pageable = PageRequest.of(page, size, Sort.by("updatedAt").descending());
        return terminalService.listTerminals(keyword, pageable);
    }

    @GetMapping("/{terminalId}")
    public Terminal get(@PathVariable String terminalId) {
        return terminalService.getTerminal(terminalId);
    }

    @GetMapping("/{terminalId}/online")
    public Map<String, Object> onlineStatus(@PathVariable String terminalId) {
        return Map.of(
            "terminalId", terminalId,
            "online",     terminalService.isOnline(terminalId)
        );
    }

    @GetMapping("/stats/online-count")
    public Map<String, Long> onlineCount() {
        return Map.of("count", terminalService.countOnline());
    }

    /**
     * 批量查询在线状态（一次 Redis HMGET）
     *
     * @param ids 逗号分隔的 terminalId 列表，如 ?ids=id1,id2,id3
     * @return { "id1": true, "id2": false, ... }
     */
    @GetMapping("/online-status")
    public Map<String, Boolean> batchOnlineStatus(@RequestParam String ids) {
        List<String> idList = Arrays.stream(ids.split(","))
                .map(String::trim)
                .filter(s -> !s.isEmpty())
                .toList();
        return terminalService.batchIsOnline(idList);
    }

    @PatchMapping("/{terminalId}")
    public ResponseEntity<Void> update(
            @PathVariable String terminalId,
            @RequestBody Map<String, String> body) {
        terminalService.updateTerminal(terminalId,
            body.get("department"), body.get("owner"));
        return ResponseEntity.ok().build();
    }
}
