package com.example.jamkkaebi.workbench.controller;

import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.AuthPrincipal;
import com.example.jamkkaebi.workbench.dto.request.BuildingLevelUpRequest;
import com.example.jamkkaebi.workbench.dto.response.BuildingDetailResponse;
import com.example.jamkkaebi.workbench.dto.response.BuildingLevelUpResponse;
import com.example.jamkkaebi.workbench.service.BuildingService;
import jakarta.validation.Valid;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 건물 상세 조회와 강화.
 *
 * <p>건물도 유물처럼 <b>마스터 번호</b>로 가리킨다 — 한 사용자는 건물 1종당 행을 하나만 가진다.
 */
@RestController
@RequestMapping("/api/buildings")
public class BuildingController {

    private final BuildingService buildingService;

    public BuildingController(BuildingService buildingService) {
        this.buildingService = buildingService;
    }

    @GetMapping("/{buildingId}")
    public ResponseEntity<ApiResponse<BuildingDetailResponse>> getDetail(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable Integer buildingId
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "건물 정보를 조회했습니다.",
                buildingService.detail(principal.friendCode(), buildingId)));
    }

    @PostMapping("/{buildingId}/level-up")
    public ResponseEntity<ApiResponse<BuildingLevelUpResponse>> levelUp(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable Integer buildingId,
            @Valid @RequestBody BuildingLevelUpRequest request
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "BUILDING_LEVELED_UP", "건물을 강화했습니다.",
                buildingService.levelUp(principal.friendCode(), buildingId, request.targetLevel())));
    }
}
