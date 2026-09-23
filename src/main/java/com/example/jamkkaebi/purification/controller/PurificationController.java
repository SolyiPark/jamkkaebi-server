package com.example.jamkkaebi.purification.controller;

import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.AuthPrincipal;
import com.example.jamkkaebi.purification.dto.request.PurificationResultRequest;
import com.example.jamkkaebi.purification.dto.request.PurificationStartRequest;
import com.example.jamkkaebi.purification.dto.response.DifficultyListResponse;
import com.example.jamkkaebi.purification.dto.response.PurificationResultResponse;
import com.example.jamkkaebi.purification.dto.response.PurificationStartResponse;
import com.example.jamkkaebi.purification.service.PurificationService;
import jakarta.validation.Valid;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RestController;

/**
 * 유물 정화
 */
@RestController
public class PurificationController {

    private final PurificationService purificationService;

    public PurificationController(PurificationService purificationService) {
        this.purificationService = purificationService;
    }

    @GetMapping("/api/difficulties")
    public ResponseEntity<ApiResponse<DifficultyListResponse>> getDifficulties() {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "난이도 정보를 조회했습니다.", purificationService.difficulties()));
    }

    @PostMapping("/api/purifications")
    public ResponseEntity<ApiResponse<PurificationStartResponse>> start(
            @AuthenticationPrincipal AuthPrincipal principal,
            @Valid @RequestBody PurificationStartRequest request
    ) {
        PurificationStartResponse response = purificationService.start(principal.friendCode(), request);
        return ResponseEntity.status(HttpStatus.CREATED).body(ApiResponse.success(
                "PURIFICATION_STARTED", "정화를 시작했습니다.", response));
    }

    @PostMapping("/api/purifications/{sessionToken}/result")
    public ResponseEntity<ApiResponse<PurificationResultResponse>> report(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable String sessionToken,
            @Valid @RequestBody PurificationResultRequest request
    ) {
        PurificationResultResponse response =
                purificationService.report(principal.friendCode(), sessionToken, request);
        return ResponseEntity.ok(ApiResponse.success(
                "PURIFICATION_SETTLED", "정화 결과를 정산했습니다.", response));
    }
}
