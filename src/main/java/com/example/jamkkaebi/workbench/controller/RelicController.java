package com.example.jamkkaebi.workbench.controller;

import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.AuthPrincipal;
import com.example.jamkkaebi.workbench.dto.request.AwakenRequest;
import com.example.jamkkaebi.workbench.dto.request.SpiritNameUpdateRequest;
import com.example.jamkkaebi.workbench.dto.response.RelicDetailResponse;
import com.example.jamkkaebi.workbench.dto.response.AwakenResponse;
import com.example.jamkkaebi.workbench.dto.response.SpiritNameResponse;
import com.example.jamkkaebi.workbench.service.WorkbenchService;
import jakarta.validation.Valid;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PatchMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 상세 조회, 이름 짓기, 강화 확정.
 *
 * <p>유물은 <b>마스터 번호</b>로 가리킨다.
 */
@RestController
@RequestMapping("/api/relics")
public class RelicController {

    private final WorkbenchService workbenchService;

    public RelicController(WorkbenchService workbenchService) {
        this.workbenchService = workbenchService;
    }

    @GetMapping("/{relicId}")
    public ResponseEntity<ApiResponse<RelicDetailResponse>> getDetail(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable Integer relicId
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "유물 정보를 조회했습니다.",
                workbenchService.detail(principal.friendCode(), relicId)));
    }

    @PatchMapping("/{relicId}/spirit-name")
    public ResponseEntity<ApiResponse<SpiritNameResponse>> renameSpirit(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable Integer relicId,
            @Valid @RequestBody SpiritNameUpdateRequest request
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "SPIRIT_NAMED", "도깨비의 이름을 지었습니다.",
                workbenchService.renameSpirit(principal.friendCode(), relicId, request.name())));
    }

    @PostMapping("/{relicId}/awaken")
    public ResponseEntity<ApiResponse<AwakenResponse>> awaken(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable Integer relicId,
            @Valid @RequestBody AwakenRequest request
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "SPIRIT_AWAKENED", "도깨비를 강화했습니다.",
                workbenchService.awaken(principal.friendCode(), relicId, request)));
    }
}
