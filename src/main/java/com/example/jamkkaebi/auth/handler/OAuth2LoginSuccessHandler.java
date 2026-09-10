package com.example.jamkkaebi.auth.handler;

import com.example.jamkkaebi.auth.config.AuthProperties;
import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.auth.service.HandoffService;
import com.example.jamkkaebi.auth.service.LoginSessionService;
import com.example.jamkkaebi.auth.service.SocialProfile;
import com.example.jamkkaebi.auth.service.SocialProfileExtractor;
import com.example.jamkkaebi.user.service.UserRegistrationService;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import jakarta.servlet.http.HttpSession;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.security.core.Authentication;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.oauth2.client.authentication.OAuth2AuthenticationToken;
import org.springframework.security.oauth2.core.user.OAuth2User;
import org.springframework.security.web.authentication.AuthenticationSuccessHandler;
import org.springframework.stereotype.Component;
import org.springframework.web.util.UriComponentsBuilder;

import java.io.IOException;
import java.util.Optional;

/**
 * 소셜 인증이 끝난 뒤 <b>가입 처리와 인계 코드 발급</b>을 맡는다.
 *
 * <p>여기서 JWT 를 만들어 딥링크에 실지 않는다. 딥링크 URL 은 브라우저 기록에 남고, 안드로이드에서는
 * 같은 커스텀 스킴을 등록한 다른 앱이 가로챌 수 있다. 그래서 <b>일회용 코드만</b> 흘리고, 실제 토큰은
 * 앱이 {@code POST /api/auth/exchange} 로 verifier 와 함께 받아 간다.
 */
@Component
public class OAuth2LoginSuccessHandler implements AuthenticationSuccessHandler {

    private static final Logger log = LoggerFactory.getLogger(OAuth2LoginSuccessHandler.class);

    /** 딥링크로 돌려주는 오류 사유. 클라이언트는 이 값을 보고 안내 문구를 고른다. */
    private static final String ERROR_INVALID_SESSION = "invalid_login_session";
    private static final String ERROR_LOGIN_FAILED = "login_failed";

    private final LoginSessionService loginSessionService;
    private final SocialProfileExtractor socialProfileExtractor;
    private final UserRegistrationService userRegistrationService;
    private final HandoffService handoffService;
    private final AuthProperties authProperties;

    public OAuth2LoginSuccessHandler(LoginSessionService loginSessionService,
                                     SocialProfileExtractor socialProfileExtractor,
                                     UserRegistrationService userRegistrationService,
                                     HandoffService handoffService,
                                     AuthProperties authProperties) {
        this.loginSessionService = loginSessionService;
        this.socialProfileExtractor = socialProfileExtractor;
        this.userRegistrationService = userRegistrationService;
        this.handoffService = handoffService;
        this.authProperties = authProperties;
    }

    @Override
    public void onAuthenticationSuccess(HttpServletRequest request,
                                        HttpServletResponse response,
                                        Authentication authentication) throws IOException {
        // 이 로그인을 시작한 앱이 맡겨 둔 verifier 해시를 찾는다. 없으면 인계할 상대를 확인할 수
        // 없다는 뜻이므로 토큰을 만들지 않는다.
        Optional<String> verifierHash =
                loginSessionService.consume(request.getParameter("state"));

        // 소셜 로그인 자체는 인계를 위한 절차일 뿐이라 서버 세션을 남기지 않는다. 남겨 두면
        // 브라우저 쿠키만으로 API 에 접근할 수 있는 두 번째 인증 경로가 생긴다.
        clearWebSession(request);

        if (verifierHash.isEmpty()) {
            log.warn("verifier 가 없는 로그인 시도라 인계 코드를 발급하지 않았습니다.");
            redirectToApp(response, deepLinkWithError(ERROR_INVALID_SESSION));
            return;
        }

        try {
            UserRegistrationService.Registration registration = register(authentication);
            String handoff = handoffService.issue(
                    registration.user().getId(), verifierHash.get(), registration.newUser());
            redirectToApp(response, UriComponentsBuilder
                    .fromUriString(authProperties.deepLinkUri())
                    .queryParam("code", handoff)
                    .build()
                    .encode()
                    .toUriString());
        } catch (RuntimeException exception) {
            // 여기서 500 페이지를 띄우면 사용자는 브라우저에 갇힌다. 앱으로 돌려보내 다시 시도하게
            // 하고, 원인은 서버 로그에 남긴다.
            log.error("소셜 로그인 후처리에 실패했습니다.", exception);
            redirectToApp(response, deepLinkWithError(ERROR_LOGIN_FAILED));
        }
    }

    private UserRegistrationService.Registration register(Authentication authentication) {
        OAuth2AuthenticationToken token = (OAuth2AuthenticationToken) authentication;
        AuthProvider provider =
                AuthProvider.fromRegistrationId(token.getAuthorizedClientRegistrationId());

        OAuth2User oAuth2User = (OAuth2User) authentication.getPrincipal();
        SocialProfile profile =
                socialProfileExtractor.extract(provider, oAuth2User.getAttributes());

        return userRegistrationService.findOrRegister(
                provider, profile.providerUserId(), profile.rawNickname());
    }

    private String deepLinkWithError(String reason) {
        return UriComponentsBuilder.fromUriString(authProperties.deepLinkUri())
                .queryParam("error", reason)
                .build()
                .encode()
                .toUriString();
    }

    private void redirectToApp(HttpServletResponse response, String location) throws IOException {
        response.sendRedirect(location);
    }

    private void clearWebSession(HttpServletRequest request) {
        SecurityContextHolder.clearContext();
        HttpSession session = request.getSession(false);
        if (session != null) {
            session.invalidate();
        }
    }
}
