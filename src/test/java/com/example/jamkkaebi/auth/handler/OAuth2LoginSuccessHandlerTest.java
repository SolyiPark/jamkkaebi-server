package com.example.jamkkaebi.auth.handler;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.auth.repository.AuthHandoffRepository;
import com.example.jamkkaebi.auth.repository.LoginSessionRepository;
import com.example.jamkkaebi.auth.service.HandoffService;
import com.example.jamkkaebi.auth.service.LoginSessionService;
import com.example.jamkkaebi.common.policy.TokenHasher;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.mock.web.MockHttpServletResponse;
import org.springframework.security.core.Authentication;
import org.springframework.security.core.authority.AuthorityUtils;
import org.springframework.security.oauth2.client.authentication.OAuth2AuthenticationToken;
import org.springframework.security.oauth2.core.OAuth2AuthenticationException;
import org.springframework.security.oauth2.core.user.DefaultOAuth2User;

import java.util.Map;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 소셜 인증이 끝난 <b>직후</b>를 검증한다 — 가입과 인계 코드 발급이 일어나는 자리다.
 *
 * <p>제공자와의 왕복(동의 화면·인가코드 교환)은 Spring Security 가 처리하고 테스트에서 재현할 수
 * 없으므로, 그 결과물인 {@code Authentication} 을 직접 만들어 핸들러에 넘긴다. 그러면 여기서만
 * 일어나는 일 — verifier 세션 대조, 제공자별 프로필 해석, 자동 가입, 딥링크 인계 — 을 전부 볼 수 있다.
 */
@SpringBootTest
class OAuth2LoginSuccessHandlerTest {

    private static final String VERIFIER = "unity-random-verifier-0123456789abcdef";
    private static final String DEEP_LINK_PREFIX = "jamkkaebi://auth?";

    @Autowired
    private OAuth2LoginSuccessHandler successHandler;

    @Autowired
    private OAuth2LoginFailureHandler failureHandler;

    @Autowired
    private LoginSessionService loginSessionService;

    @Autowired
    private HandoffService handoffService;

    @Autowired
    private UserRepository userRepository;

    @Autowired
    private AuthHandoffRepository authHandoffRepository;

    @Autowired
    private LoginSessionRepository loginSessionRepository;

    @AfterEach
    void clear() {
        authHandoffRepository.deleteAll();
        loginSessionRepository.deleteAll();
        userRepository.deleteAll();
    }

    @Test
    @DisplayName("구글 최초 로그인이면 가입시키고 인계 코드를 딥링크로 돌려준다")
    void registersAndHandsOffOnFirstGoogleLogin() throws Exception {
        String state = startLoginSession();

        MockHttpServletResponse response =
                succeed(state, googleAuthentication("g-success-1", "구글잠깨비"));

        String code = queryValue(response.getRedirectedUrl(), "code");
        HandoffService.Handoff handoff = handoffService.exchange(code, VERIFIER);

        assertThat(handoff.newUser()).isTrue();
        User user = userRepository.findById(handoff.userId()).orElseThrow();
        assertThat(user.getProvider()).isEqualTo(AuthProvider.GOOGLE);
        assertThat(user.getProviderUserId()).isEqualTo("g-success-1");
        assertThat(user.getNickname()).isEqualTo("구글잠깨비");
        assertThat(user.getFriendCode()).hasSize(User.FRIEND_CODE_LENGTH);
    }

    @Test
    @DisplayName("카카오는 중첩된 응답에서 회원번호와 닉네임을 꺼내 가입시킨다")
    void registersFromNestedKakaoAttributes() throws Exception {
        String state = startLoginSession();

        MockHttpServletResponse response =
                succeed(state, kakaoAuthentication(4_100_200_300L, "카카오잠깨비"));

        HandoffService.Handoff handoff =
                handoffService.exchange(queryValue(response.getRedirectedUrl(), "code"), VERIFIER);
        User user = userRepository.findById(handoff.userId()).orElseThrow();

        assertThat(user.getProvider()).isEqualTo(AuthProvider.KAKAO);
        assertThat(user.getProviderUserId()).isEqualTo("4100200300");
        assertThat(user.getNickname()).isEqualTo("카카오잠깨비");
    }

    @Test
    @DisplayName("재로그인은 같은 계정을 쓰고 newUser 가 아니다 — 수정 화면이 다시 뜨면 안 된다")
    void secondLoginIsNotNewUser() throws Exception {
        HandoffService.Handoff first = handoffService.exchange(
                queryValue(succeed(startLoginSession(),
                        googleAuthentication("g-success-2", "구글잠깨비")).getRedirectedUrl(), "code"),
                VERIFIER);
        HandoffService.Handoff second = handoffService.exchange(
                queryValue(succeed(startLoginSession(),
                        googleAuthentication("g-success-2", "구글잠깨비")).getRedirectedUrl(), "code"),
                VERIFIER);

        assertThat(first.newUser()).isTrue();
        assertThat(second.newUser()).isFalse();
        assertThat(second.userId()).isEqualTo(first.userId());
        assertThat(userRepository.count()).isEqualTo(1);
    }

    @Test
    @DisplayName("이 로그인을 시작한 앱을 확인할 수 없으면 인계 코드를 만들지 않는다")
    void refusesHandoffWithoutLoginSession() throws Exception {
        // verifier_hash 없이 인증을 시작한 경우다. 소셜 인증 자체는 통과했지만, 결과를 넘겨줄
        // 상대를 확인할 수 없으므로 여기서 끊어야 한다.
        MockHttpServletResponse response =
                succeed("state-without-session", googleAuthentication("g-success-3", "구글잠깨비"));

        assertThat(response.getRedirectedUrl())
                .isEqualTo(DEEP_LINK_PREFIX + "error=invalid_login_session");
        assertThat(authHandoffRepository.count()).isZero();
        assertThat(userRepository.count()).isZero();
    }

    @Test
    @DisplayName("한 번 쓴 로그인 세션은 사라진다 — 같은 state 로 인계 코드를 두 번 만들 수 없다")
    void consumesLoginSessionOnce() throws Exception {
        String state = startLoginSession();

        succeed(state, googleAuthentication("g-success-4", "구글잠깨비"));
        MockHttpServletResponse replay =
                succeed(state, googleAuthentication("g-success-4", "구글잠깨비"));

        assertThat(loginSessionRepository.findById(state)).isEmpty();
        assertThat(replay.getRedirectedUrl())
                .isEqualTo(DEEP_LINK_PREFIX + "error=invalid_login_session");
    }

    @Test
    @DisplayName("인증에 실패하면 사유를 감춘 채 앱으로 돌려보내고 세션을 정리한다")
    void failureReturnsToAppWithoutLeakingReason() throws Exception {
        String state = startLoginSession();

        MockHttpServletRequest request = new MockHttpServletRequest();
        request.setParameter("state", state);
        MockHttpServletResponse response = new MockHttpServletResponse();
        failureHandler.onAuthenticationFailure(request, response,
                new OAuth2AuthenticationException("invalid_client: secret 이 틀렸습니다"));

        assertThat(response.getRedirectedUrl()).isEqualTo(DEEP_LINK_PREFIX + "error=login_failed");
        assertThat(loginSessionRepository.findById(state)).isEmpty();
    }

    // ---------- 도우미 ----------

    /** 클라이언트가 verifier 해시를 맡긴 상태를 만든다. */
    private String startLoginSession() {
        String state = UUID.randomUUID().toString();
        loginSessionService.start(state, TokenHasher.sha256(VERIFIER));
        return state;
    }

    private MockHttpServletResponse succeed(String state, Authentication authentication)
            throws Exception {
        MockHttpServletRequest request = new MockHttpServletRequest();
        request.setParameter("state", state);
        MockHttpServletResponse response = new MockHttpServletResponse();
        successHandler.onAuthenticationSuccess(request, response, authentication);
        return response;
    }

    /** 구글은 {@code openid} 스코프라 OIDC 표준 클레임으로 온다. */
    private Authentication googleAuthentication(String sub, String name) {
        return new OAuth2AuthenticationToken(
                new DefaultOAuth2User(
                        AuthorityUtils.NO_AUTHORITIES, Map.of("sub", sub, "name", name), "sub"),
                AuthorityUtils.NO_AUTHORITIES,
                "google");
    }

    /** 카카오는 회원번호가 최상위 {@code id}(숫자), 닉네임은 두 겹 안에 있다. */
    private Authentication kakaoAuthentication(long id, String nickname) {
        return new OAuth2AuthenticationToken(
                new DefaultOAuth2User(
                        AuthorityUtils.NO_AUTHORITIES,
                        Map.of("id", id,
                                "kakao_account", Map.of("profile", Map.of("nickname", nickname))),
                        "id"),
                AuthorityUtils.NO_AUTHORITIES,
                "kakao");
    }

    private String queryValue(String url, String name) {
        assertThat(url).startsWith(DEEP_LINK_PREFIX);
        String query = url.substring(DEEP_LINK_PREFIX.length());
        assertThat(query).startsWith(name + "=");
        return query.substring(name.length() + 1);
    }
}
