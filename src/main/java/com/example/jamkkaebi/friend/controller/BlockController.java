package com.example.jamkkaebi.friend.controller;

import com.example.jamkkaebi.friend.dto.request.FriendCodeRequest;
import com.example.jamkkaebi.friend.dto.response.BlockListResponse;
import com.example.jamkkaebi.friend.service.BlockService;
import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.AuthPrincipal;
import jakarta.validation.Valid;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/** 차단 · 차단 목록 · 차단 해제. */
@RestController
@RequestMapping("/api/blocks")
public class BlockController {

    private final BlockService blockService;

    public BlockController(BlockService blockService) {
        this.blockService = blockService;
    }

    @PostMapping
    public ResponseEntity<ApiResponse<Void>> block(
            @AuthenticationPrincipal AuthPrincipal principal,
            @Valid @RequestBody FriendCodeRequest request
    ) {
        blockService.block(principal.friendCode(), request.friendCode());
        return ResponseEntity.ok(ApiResponse.success("USER_BLOCKED", "차단했습니다.", null));
    }

    @GetMapping
    public ResponseEntity<ApiResponse<BlockListResponse>> list(
            @AuthenticationPrincipal AuthPrincipal principal
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "차단 목록을 조회했습니다.", blockService.list(principal.friendCode())));
    }

    @DeleteMapping("/{friendCode}")
    public ResponseEntity<ApiResponse<Void>> unblock(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable String friendCode
    ) {
        blockService.unblock(principal.friendCode(), friendCode);
        return ResponseEntity.ok(ApiResponse.success("USER_UNBLOCKED", "차단을 해제했습니다.", null));
    }
}
