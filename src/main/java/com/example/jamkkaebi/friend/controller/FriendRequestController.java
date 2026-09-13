package com.example.jamkkaebi.friend.controller;

import com.example.jamkkaebi.friend.domain.RequestDirection;
import com.example.jamkkaebi.friend.dto.request.FriendCodeRequest;
import com.example.jamkkaebi.friend.dto.response.FriendRequestCancelResponse;
import com.example.jamkkaebi.friend.dto.response.FriendRequestListResponse;
import com.example.jamkkaebi.friend.dto.response.FriendRequestResponse;
import com.example.jamkkaebi.friend.service.FriendRequestService;
import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.AuthPrincipal;
import jakarta.validation.Valid;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

/**
 * 친구 요청 보내기 · 요청함 · 수락 · 거절 · 취소.
 *
 * <p>두 사람 사이의 대기 요청은 교차 자동 수락 규칙 때문에 최대 1건이라, 응답하거나 취소할 요청을 요청
 * id 가 아니라 상대의 친구 코드로 가리킨다. 수락·거절은 받은 쪽이 요청에 <i>응답</i>하는 행위라
 * {@code POST .../accept}·{@code .../reject} 지만, 취소는 보낸 쪽이 자기가 만든 리소스를 <i>없애는</i>
 * 것이라 {@code DELETE} 다.
 */
@RestController
@RequestMapping("/api/friend-requests")
public class FriendRequestController {

    private final FriendRequestService friendRequestService;

    public FriendRequestController(FriendRequestService friendRequestService) {
        this.friendRequestService = friendRequestService;
    }

    @PostMapping
    public ResponseEntity<ApiResponse<FriendRequestResponse>> send(
            @AuthenticationPrincipal AuthPrincipal principal,
            @Valid @RequestBody FriendCodeRequest request
    ) {
        FriendRequestService.SendResult result =
                friendRequestService.send(principal.friendCode(), request.friendCode());
        // 상대 요청이 이미 와 있어 바로 친구가 된 경우
        if (result.friendAdded()) {
            return ResponseEntity.ok(ApiResponse.success("FRIEND_ADDED", "친구가 되었습니다.", result.response()));
        }
        return ResponseEntity.status(HttpStatus.CREATED)
                .body(ApiResponse.success("FRIEND_REQUEST_SENT", "친구 요청을 보냈습니다.", result.response()));
    }

    @GetMapping
    public ResponseEntity<ApiResponse<FriendRequestListResponse>> list(
            @AuthenticationPrincipal AuthPrincipal principal,
            @RequestParam RequestDirection direction
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "친구 요청 목록을 조회했습니다.", friendRequestService.list(principal.friendCode(), direction)));
    }

    @PostMapping("/{friendCode}/accept")
    public ResponseEntity<ApiResponse<FriendRequestResponse>> accept(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable String friendCode
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "FRIEND_ADDED", "친구가 되었습니다.", friendRequestService.accept(principal.friendCode(), friendCode)));
    }

    @PostMapping("/{friendCode}/reject")
    public ResponseEntity<ApiResponse<Void>> reject(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable String friendCode
    ) {
        friendRequestService.reject(principal.friendCode(), friendCode);
        return ResponseEntity.ok(ApiResponse.success("FRIEND_REQUEST_REJECTED", "친구 요청을 거절했습니다.", null));
    }

    @DeleteMapping("/{friendCode}")
    public ResponseEntity<ApiResponse<FriendRequestCancelResponse>> cancel(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable String friendCode
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "FRIEND_REQUEST_CANCELED",
                "보낸 요청을 취소했습니다.",
                friendRequestService.cancel(principal.friendCode(), friendCode)));
    }
}
