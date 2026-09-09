package com.example.jamkkaebi.auth.controller;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.auth.dto.response.AuthTokenResponse;
import com.example.jamkkaebi.auth.service.RefreshTokenService;
import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.JwtProvider;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.service.UserRegistrationService;
import org.springframework.context.annotation.Profile;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 개발 환경 전용 토큰 발급.
 *
 * <p>커스텀 스킴 딥링크는 실제 기기·에뮬레이터에서만 동작한다. 그래서 딥링크를 받을 수 없는 개발
 * 환경에서도 브라우저 왕복 없이 바로 토큰을 받아 보호 API 를 붙여 볼 수 있어야 한다.
 *
 * <p><b>{@code dev} 프로파일에서만 빈이 등록된다.</b> 운영 프로파일에서는 컨트롤러 자체가 없어
 * 경로가 존재하지 않는다 — 인증 없이 토큰을 내주는 문이라, 설정 실수 하나로 열리게 두면 안 된다.
 */
@RestController
@RequestMapping("/dev")
@Profile("dev")
public class DevTokenController {

    /** 개발용 계정의 소셜 회원번호 자리. 실제 소셜 계정과 겹치지 않게 접두어를 붙인다. */
    private static final String DEV_PROVIDER_USER_ID_PREFIX = "dev-";
    private static final String DEFAULT_DEV_KEY = "editor";
    private static final String DEFAULT_DEV_NICKNAME = "개발잠깨비";
    /** 이 경로로 나간 토큰임을 세션 목록에서 알아볼 수 있게 붙이는 표시. */
    private static final String DEV_DEVICE_LABEL = "dev-token";

    private final UserRegistrationService userRegistrationService;
    private final JwtProvider jwtProvider;
    private final RefreshTokenService refreshTokenService;

    public DevTokenController(UserRegistrationService userRegistrationService,
                              JwtProvider jwtProvider,
                              RefreshTokenService refreshTokenService) {
        this.userRegistrationService = userRegistrationService;
        this.jwtProvider = jwtProvider;
        this.refreshTokenService = refreshTokenService;
    }

    /**
     * 지정한 키(생략 시 기본값)의 개발용 사용자로 토큰을 발급한다. 키를 바꾸면 다른 계정이 되므로
     * 친구 기능처럼 두 계정이 필요한 화면도 개발 환경에서 테스트할 수 있다.
     */
    @PostMapping("/token")
    public ResponseEntity<ApiResponse<AuthTokenResponse>> issue(
            @RequestBody(required = false) DevTokenRequest request
    ) {
        String key = (request == null || request.key() == null || request.key().isBlank())
                ? DEFAULT_DEV_KEY : request.key().trim();

        UserRegistrationService.Registration registration = userRegistrationService.findOrRegister(
                AuthProvider.GOOGLE, DEV_PROVIDER_USER_ID_PREFIX + key, DEFAULT_DEV_NICKNAME);
        User user = registration.user();

        String accessToken = jwtProvider.createAccessToken(user.getFriendCode());
        String refreshToken = jwtProvider.createRefreshToken(user.getFriendCode());
        refreshTokenService.save(
                user.getId(), refreshToken, jwtProvider.getRefreshTokenValidityMs(), DEV_DEVICE_LABEL);

        return ResponseEntity.ok(ApiResponse.success(
                "DEV_TOKEN_ISSUED", "개발용 토큰을 발급했습니다.",
                new AuthTokenResponse(
                        accessToken,
                        refreshToken,
                        jwtProvider.getAccessTokenValidityMs() / 1000L,
                        user.getFriendCode(),
                        user.getNickname(),
                        registration.newUser())));
    }

    /**
     * @param key 개발용 계정을 구분하는 이름. 생략하면 기본 계정이다.
     */
    public record DevTokenRequest(String key) {
    }
}
