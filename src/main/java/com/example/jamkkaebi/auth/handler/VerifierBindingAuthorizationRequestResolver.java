package com.example.jamkkaebi.auth.handler;

import com.example.jamkkaebi.auth.service.LoginSessionService;
import jakarta.servlet.http.HttpServletRequest;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.security.oauth2.client.registration.ClientRegistrationRepository;
import org.springframework.security.oauth2.client.web.DefaultOAuth2AuthorizationRequestResolver;
import org.springframework.security.oauth2.client.web.OAuth2AuthorizationRequestResolver;
import org.springframework.security.oauth2.core.endpoint.OAuth2AuthorizationRequest;
import org.springframework.stereotype.Component;

/**
 * 소셜 인증을 시작할 때 클라이언트가 보낸 {@code verifier_hash} 를 그 요청의 {@code state} 에 매어 둔다.
 *
 * <p>인가 URL 생성과 {@code state} 발급은 Spring Security 가 하던 대로 두고, 이 클래스는 <b>발급된
 * state 에 verifier 해시를 붙이는 일만</b> 한다. 그래야 로그인이 끝났을 때 "이 로그인을 시작한
 * 그 앱"에게만 인계 코드를 넘겨줄 수 있다.
 *
 * <p>해시가 없으면 세션을 만들지 않고 그대로 진행시킨다. 여기서 예외를 던지면 필터 단계라
 * 공통 오류 응답을 태울 수 없어 500 이 나가기 때문이다. 대신 인계 단계
 * ({@link OAuth2LoginSuccessHandler})에서 세션이 없는 로그인을 거절한다 — 거기서는 딥링크로
 * 오류를 돌려줄 수 있다.
 */
@Component
public class VerifierBindingAuthorizationRequestResolver implements OAuth2AuthorizationRequestResolver {

    /** 클라이언트가 로그인 시작 요청에 싣는 파라미터 이름. */
    public static final String VERIFIER_HASH_PARAMETER = "verifier_hash";

    private static final Logger log =
            LoggerFactory.getLogger(VerifierBindingAuthorizationRequestResolver.class);

    /** Spring Security 의 기본 인가 엔드포인트 경로. */
    private static final String AUTHORIZATION_BASE_URI = "/oauth2/authorization";

    private final OAuth2AuthorizationRequestResolver delegate;
    private final LoginSessionService loginSessionService;

    public VerifierBindingAuthorizationRequestResolver(
            ClientRegistrationRepository clientRegistrationRepository,
            LoginSessionService loginSessionService) {
        this.delegate = new DefaultOAuth2AuthorizationRequestResolver(
                clientRegistrationRepository, AUTHORIZATION_BASE_URI);
        this.loginSessionService = loginSessionService;
    }

    @Override
    public OAuth2AuthorizationRequest resolve(HttpServletRequest request) {
        return bindVerifier(delegate.resolve(request), request);
    }

    @Override
    public OAuth2AuthorizationRequest resolve(HttpServletRequest request, String clientRegistrationId) {
        return bindVerifier(delegate.resolve(request, clientRegistrationId), request);
    }

    private OAuth2AuthorizationRequest bindVerifier(OAuth2AuthorizationRequest authorizationRequest,
                                                    HttpServletRequest request) {
        if (authorizationRequest == null) {
            return null;
        }
        String verifierHash = request.getParameter(VERIFIER_HASH_PARAMETER);
        if (verifierHash == null || verifierHash.isBlank()) {
            log.warn("verifier_hash 없이 소셜 로그인이 시작됐습니다. 인계 단계에서 거절됩니다.");
            return authorizationRequest;
        }
        loginSessionService.start(authorizationRequest.getState(), verifierHash);
        return authorizationRequest;
    }
}
