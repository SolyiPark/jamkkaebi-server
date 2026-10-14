package com.example.jamkkaebi.user.controller;

import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.AuthPrincipal;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.dto.request.AvatarUpdateRequest;
import com.example.jamkkaebi.user.dto.request.NicknameUpdateRequest;
import com.example.jamkkaebi.user.dto.response.MyProfileResponse;
import com.example.jamkkaebi.user.service.AvatarService;
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
    private final AvatarService avatarService;

    public UserController(UserProfileService userProfileService, AvatarService avatarService) {
        this.userProfileService = userProfileService;
        this.avatarService = avatarService;
    }

    @GetMapping("/me")
    public ResponseEntity<ApiResponse<MyProfileResponse>> getMyProfile(
            @AuthenticationPrincipal AuthPrincipal principal
    ) {
        User user = userProfileService.getByFriendCode(principal.friendCode());
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "프로필을 조회했습니다.", MyProfileResponse.from(user)));
    }

    /**
     * 닉네임 변경
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

    /**
     * 프로필 아바타 변경. 기본이거나 정령 수호까지 마친 도깨비 중에서 고른다.
     */
    @PatchMapping("/me/avatar")
    public ResponseEntity<ApiResponse<MyProfileResponse>> updateAvatar(
            @AuthenticationPrincipal AuthPrincipal principal,
            @RequestBody AvatarUpdateRequest request
    ) {
        User user = avatarService.change(principal.friendCode(), request.spiritId());
        return ResponseEntity.ok(ApiResponse.success(
                "AVATAR_UPDATED", "프로필 아바타를 바꿨습니다.", MyProfileResponse.from(user)));
    }
}
