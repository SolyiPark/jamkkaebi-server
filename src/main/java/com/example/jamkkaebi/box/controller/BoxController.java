package com.example.jamkkaebi.box.controller;

import com.example.jamkkaebi.box.dto.request.BoxOpenRequest;
import com.example.jamkkaebi.box.dto.response.BoxOpenResponse;
import com.example.jamkkaebi.box.dto.response.BoxStatusResponse;
import com.example.jamkkaebi.box.service.BoxService;
import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.AuthPrincipal;
import jakarta.validation.Valid;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 일일 상자 — 상태 조회와 열기.
 *
 * <p>하루 1~2회만 쓰는 동작이라 전시관 최상단에 붙는다.
 */
@RestController
@RequestMapping("/api/box")
public class BoxController {

    private final BoxService boxService;

    public BoxController(BoxService boxService) {
        this.boxService = boxService;
    }

    @GetMapping
    public ResponseEntity<ApiResponse<BoxStatusResponse>> getStatus(
            @AuthenticationPrincipal AuthPrincipal principal
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "오늘의 상자 상태를 조회했습니다.", boxService.status(principal.friendCode())));
    }

    @PostMapping("/open")
    public ResponseEntity<ApiResponse<BoxOpenResponse>> open(
            @AuthenticationPrincipal AuthPrincipal principal,
            @Valid @RequestBody BoxOpenRequest request
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "BOX_OPENED", "상자를 열었습니다.", boxService.open(principal.friendCode(), request)));
    }
}
