package com.example.jamkkaebi.auth.handler;

import com.example.jamkkaebi.auth.config.AuthProperties;
import com.example.jamkkaebi.auth.service.LoginSessionService;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.security.core.AuthenticationException;
import org.springframework.security.web.authentication.AuthenticationFailureHandler;
import org.springframework.stereotype.Component;
import org.springframework.web.util.UriComponentsBuilder;

import java.io.IOException;

/**
 * 소셜 인증이 실패했을 때 사용자를 게임 앱으로 돌려보낸다.
 *
 * <p>사용자가 동의 화면에서 취소했거나, {@code state} 검증이 깨졌거나, 제공자가 오류를 준 경우가
 * 여기로 온다. 기본 동작대로 두면 브라우저에 로그인 페이지나 오류 화면이 남아 사용자가 앱으로
 * 돌아올 방법이 없다.
 *
 * <p><b>실패 사유를 그대로 앱에 넘기지 않는다.</b> 사용자가 할 수 있는 일은 어느 경우든 다시
 * 로그인하는 것뿐이고, 제공자 오류 메시지에는 클라이언트 설정 정보가 섞여 나올 수 있다.
 * 자세한 내용은 서버 로그에만 남긴다.
 */
@Component
public class OAuth2LoginFailureHandler implements AuthenticationFailureHandler {

    private static final Logger log = LoggerFactory.getLogger(OAuth2LoginFailureHandler.class);

    private static final String ERROR_LOGIN_FAILED = "login_failed";

    private final AuthProperties authProperties;
    private final LoginSessionService loginSessionService;

    public OAuth2LoginFailureHandler(AuthProperties authProperties,
                                     LoginSessionService loginSessionService) {
        this.authProperties = authProperties;
        this.loginSessionService = loginSessionService;
    }

    @Override
    public void onAuthenticationFailure(HttpServletRequest request,
                                        HttpServletResponse response,
                                        AuthenticationException exception) throws IOException {
        // 실패한 로그인의 세션은 쓸 일이 없다. 남겨 두면 만료될 때까지 자리만 차지한다.
        loginSessionService.consume(request.getParameter("state"));

        log.warn("소셜 로그인에 실패했습니다.", exception);
        response.sendRedirect(UriComponentsBuilder
                .fromUriString(authProperties.deepLinkUri())
                .queryParam("error", ERROR_LOGIN_FAILED)
                .build()
                .encode()
                .toUriString());
    }
}
