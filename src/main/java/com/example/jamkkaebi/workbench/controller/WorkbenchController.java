package com.example.jamkkaebi.workbench.controller;

import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.AuthPrincipal;
import com.example.jamkkaebi.workbench.dto.response.WorkbenchResponse;
import com.example.jamkkaebi.workbench.service.WorkbenchService;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 작업대 목록.
 */
@RestController
@RequestMapping("/api/workbench")
public class WorkbenchController {

    private final WorkbenchService workbenchService;

    public WorkbenchController(WorkbenchService workbenchService) {
        this.workbenchService = workbenchService;
    }

    @GetMapping
    public ResponseEntity<ApiResponse<WorkbenchResponse>> getWorkbench(
            @AuthenticationPrincipal AuthPrincipal principal
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "작업대를 조회했습니다.", workbenchService.workbench(principal.friendCode())));
    }
}
