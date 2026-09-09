package com.example.jamkkaebi.user.controller;

import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.AuthPrincipal;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.dto.request.NicknameUpdateRequest;
import com.example.jamkkaebi.user.dto.response.MyProfileResponse;
import com.example.jamkkaebi.user.service.UserProfileService;
import jakarta.validation.Valid;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PatchMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 내 프로필 조회·수정 API. 로그인이 실제로 붙었는지 확인하는 가장 짧은 보호 엔드포인트이기도 하다.
 */
@RestController
@RequestMapping("/api/users")
public class UserController {

    private final UserProfileService userProfileService;

    public UserController(UserProfileService userProfileService) {
        this.userProfileService = userProfileService;
    }

    @GetMapping("/me")
    public ResponseEntity<ApiResponse<MyProfileResponse>> getMyProfile(
            @AuthenticationPrincipal AuthPrincipal principal
    ) {
        User user = userProfileService.getByFriendCode(principal.friendCode());
        return ResponseEntity.ok(ApiResponse.success(
                "PROFILE_FOUND", "프로필을 조회했습니다.", MyProfileResponse.from(user)));
    }

    /**
     * 닉네임 변경.
     *
     * <p>로그인 응답의 {@code newUser} 가 참일 때 클라이언트가 띄우는 수정 화면이 여기로 보낸다.
     * 소셜에서 받은 이름은 절단·보정을 거친 값이라 사용자가 고른 이름이 아니기 때문이다. 물론
     * 그 뒤 아무 때나 다시 바꿀 수도 있다.
     */
    @PatchMapping("/me/nickname")
    public ResponseEntity<ApiResponse<MyProfileResponse>> updateNickname(
            @AuthenticationPrincipal AuthPrincipal principal,
            @Valid @RequestBody NicknameUpdateRequest request
    ) {
        User user = userProfileService.updateNickname(principal.friendCode(), request.nickname());
        return ResponseEntity.ok(ApiResponse.success(
                "NICKNAME_UPDATED", "닉네임을 변경했습니다.", MyProfileResponse.from(user)));
    }
}
