package com.example.jamkkaebi.auth.controller;

import com.example.jamkkaebi.auth.dto.request.LoginStartRequest;
import com.example.jamkkaebi.auth.dto.request.TokenExchangeRequest;
import com.example.jamkkaebi.auth.dto.request.TokenReissueRequest;
import com.example.jamkkaebi.auth.dto.response.AuthTokenResponse;
import com.example.jamkkaebi.auth.dto.response.LoginStartResponse;
import com.example.jamkkaebi.auth.handler.VerifierBindingAuthorizationRequestResolver;
import com.example.jamkkaebi.auth.service.AuthService;
import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.AuthPrincipal;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.validation.Valid;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.servlet.support.ServletUriComponentsBuilder;

import java.util.Locale;

/**
 * 소셜 로그인 · 토큰 재발급 · 로그아웃 API.
 *
 * <p>클라이언트 흐름:
 * <ol>
 *   <li>{@code verifier} 를 무작위로 만들고 그 해시를 {@code POST /api/auth/login/start} 로 보낸다.</li>
 *   <li>응답의 {@code authorizeUrl} 을 <b>시스템 브라우저</b>로 연다. 인앱 웹뷰는 소셜 정책상
 *       막히는 경우가 있다.</li>
 *   <li>로그인이 끝나면 {@code jamkkaebi://auth?code=...} 딥링크로 돌아온다.</li>
 *   <li>{@code POST /api/auth/exchange} 에 코드와 <b>verifier 원문</b>을 보내 토큰을 받는다.</li>
 *   <li>응답의 {@code newUser} 가 참이면 닉네임 수정 화면을 띄우고
 *       {@code PATCH /api/users/me/nickname} 으로 보낸다 — 소셜에서 받은 이름은 10자로 잘리거나
 *       기본값으로 바뀐 값이라 사용자가 고른 이름이 아니다.</li>
 *   <li>이후 요청에 {@code Authorization: Bearer <accessToken>} 을 붙이고, A004(만료)를 받으면
 *       {@code /api/auth/reissue} 로 조용히 갱신한다. 남은 수명은 발급 응답의
 *       {@code accessTokenExpiresIn}(초)에 들어 있다.</li>
 *   <li>재발급이 A005 로 실패하면 그때는 처음부터 다시 로그인한다. <b>이 경로에서 A004 는 나오지
 *       않는다</b> — 만료된 Refresh Token 도 A005 로 모아 재발급을 반복하지 않게 한다.</li>
 * </ol>
 */
@RestController
@RequestMapping("/api/auth")
public class AuthController {

    private final AuthService authService;

    public AuthController(AuthService authService) {
        this.authService = authService;
    }

    /**
     * 로그인 시작 — 소셜 인증을 열 주소를 돌려준다.
     *
     * <p>주소를 서버가 만들어 주는 이유는 클라이언트가 인가 URL 형식과 {@code state} 발급 방식을
     * 알 필요가 없게 하기 위해서다. 실제 {@code state} 생성과 제공자 리다이렉트는 이 주소를 열었을 때
     * Spring Security 가 처리한다.
     */
    @PostMapping("/login/start")
    public ResponseEntity<ApiResponse<LoginStartResponse>> loginStart(
            @Valid @RequestBody LoginStartRequest request,
            HttpServletRequest servletRequest
    ) {
        String registrationId = request.provider().name().toLowerCase(Locale.ROOT);
        String authorizeUrl = ServletUriComponentsBuilder.fromContextPath(servletRequest)
                .path("/oauth2/authorization/{registrationId}")
                .queryParam(
                        VerifierBindingAuthorizationRequestResolver.VERIFIER_HASH_PARAMETER,
                        request.verifierHash())
                .buildAndExpand(registrationId)
                .encode()
                .toUriString();

        return ResponseEntity.ok(ApiResponse.success(
                "LOGIN_STARTED", "소셜 인증 주소를 발급했습니다.", new LoginStartResponse(authorizeUrl)));
    }

    /** 인계 코드 교환 — 딥링크로 받은 일회용 코드를 잠깨비 토큰으로 바꾼다. */
    @PostMapping("/exchange")
    public ResponseEntity<ApiResponse<AuthTokenResponse>> exchange(
            @Valid @RequestBody TokenExchangeRequest request
    ) {
        AuthTokenResponse tokens = authService.exchange(
                request.handoff(), request.verifier(), request.deviceLabel());
        return ResponseEntity.ok(
                ApiResponse.success("LOGIN_SUCCESS", "로그인에 성공했습니다.", tokens));
    }

    /** 토큰 재발급 — Refresh Token 을 회전시키며 Access/Refresh 를 함께 새로 준다. */
    @PostMapping("/reissue")
    public ResponseEntity<ApiResponse<AuthTokenResponse>> reissue(
            @Valid @RequestBody TokenReissueRequest request
    ) {
        AuthTokenResponse tokens =
                authService.reissue(request.refreshToken(), request.deviceLabel());
        return ResponseEntity.ok(
                ApiResponse.success("TOKEN_REISSUED", "토큰이 재발급되었습니다.", tokens));
    }

    /** 로그아웃 — 이 사용자의 Refresh Token 을 모두 폐기한다. (요청 본문 없음) */
    @PostMapping("/logout")
    public ResponseEntity<ApiResponse<Void>> logout(
            @AuthenticationPrincipal AuthPrincipal principal
    ) {
        authService.logout(principal.friendCode());
        return ResponseEntity.ok(
                ApiResponse.success("LOGOUT_SUCCESS", "로그아웃되었습니다.", null));
    }
}
