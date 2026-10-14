package com.example.jamkkaebi.exhibition.controller;

import com.example.jamkkaebi.exhibition.dto.request.ExhibitionLayoutRequest;
import com.example.jamkkaebi.exhibition.dto.request.SpiritVisibilityRequest;
import com.example.jamkkaebi.exhibition.dto.response.ExhibitionResponse;
import com.example.jamkkaebi.exhibition.dto.response.SpiritVisibilityResponse;
import com.example.jamkkaebi.exhibition.service.ExhibitionService;
import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.AuthPrincipal;
import jakarta.validation.Valid;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PatchMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 내 전시관 — 홈 화면 조회, 배치 저장, 도깨비 숨기기.
 */
@RestController
@RequestMapping("/api/exhibition")
public class ExhibitionController {

    private final ExhibitionService exhibitionService;

    public ExhibitionController(ExhibitionService exhibitionService) {
        this.exhibitionService = exhibitionService;
    }

    @GetMapping
    public ResponseEntity<ApiResponse<ExhibitionResponse>> getExhibition(
            @AuthenticationPrincipal AuthPrincipal principal
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "전시관을 조회했습니다.", exhibitionService.exhibition(principal.friendCode())));
    }

    @PutMapping("/layout")
    public ResponseEntity<ApiResponse<ExhibitionResponse>> saveLayout(
            @AuthenticationPrincipal AuthPrincipal principal,
            @Valid @RequestBody ExhibitionLayoutRequest request
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "EXHIBITION_SAVED", "전시관 배치를 저장했습니다.",
                exhibitionService.saveLayout(principal.friendCode(), request.items())));
    }

    @PatchMapping("/spirits/{relicId}/visibility")
    public ResponseEntity<ApiResponse<SpiritVisibilityResponse>> changeVisibility(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable Integer relicId,
            @Valid @RequestBody SpiritVisibilityRequest request
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "SPIRIT_VISIBILITY_UPDATED", "도깨비 표시 설정을 바꿨습니다.",
                exhibitionService.changeVisibility(principal.friendCode(), relicId, request.hidden())));
    }
}
