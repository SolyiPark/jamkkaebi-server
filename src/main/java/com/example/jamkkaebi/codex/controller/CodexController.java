package com.example.jamkkaebi.codex.controller;

import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.codex.dto.response.CodexArtifactResponse;
import com.example.jamkkaebi.codex.dto.response.CodexEraResponse;
import com.example.jamkkaebi.codex.dto.response.CodexSummaryResponse;
import com.example.jamkkaebi.codex.service.CodexService;
import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.AuthPrincipal;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 도감 — 열람 전용. 성장·강화 확정은 작업대({@code /api/artifacts}) 담당으로, 여기에는 조회만 있다.
 */
@RestController
@RequestMapping("/api/codex")
public class CodexController {

    private final CodexService codexService;

    public CodexController(CodexService codexService) {
        this.codexService = codexService;
    }

    @GetMapping
    public ResponseEntity<ApiResponse<CodexSummaryResponse>> getSummary(
            @AuthenticationPrincipal AuthPrincipal principal
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "도감을 조회했습니다.", codexService.summary(principal.friendCode())));
    }

    @GetMapping("/eras/{era}")
    public ResponseEntity<ApiResponse<CodexEraResponse>> getEra(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable Era era
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "시대별 도감을 조회했습니다.", codexService.era(principal.friendCode(), era)));
    }

    @GetMapping("/artifacts/{artifactId}")
    public ResponseEntity<ApiResponse<CodexArtifactResponse>> getArtifact(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable Integer artifactId
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "도감 카드를 조회했습니다.", codexService.artifact(principal.friendCode(), artifactId)));
    }
}
