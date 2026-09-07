package com.example.jamkkaebi.auth;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.auth.repository.AuthHandoffRepository;
import com.example.jamkkaebi.auth.repository.LoginSessionRepository;
import com.example.jamkkaebi.auth.repository.RefreshTokenRepository;
import com.example.jamkkaebi.auth.service.HandoffService;
import com.example.jamkkaebi.common.policy.TokenHasher;
import com.example.jamkkaebi.global.security.JwtProperties;
import com.example.jamkkaebi.global.security.JwtProvider;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import com.example.jamkkaebi.user.service.UserRegistrationService;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.MvcResult;

import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.Optional;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * 로그인 흐름을 HTTP 로 통과시켜 본다.
 *
 * <p>소셜 제공자와의 왕복(동의 화면·인가코드 교환)은 여기서 재현할 수 없으므로, <b>그 앞뒤</b>를
 * 검증한다 — 인증 시작이 verifier 를 state 에 매어 두는지, 그리고 로그인이 끝났다고 가정했을 때
 * 인계 코드 → 토큰 → 보호 API → 재발급이 실제로 이어지는지.
 */
@SpringBootTest
@AutoConfigureMockMvc
class AuthFlowTest {

    private static final String VERIFIER = "unity-random-verifier-abcdef";

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private UserRegistrationService userRegistrationService;

    @Autowired
    private HandoffService handoffService;

    @Autowired
    private JwtProperties jwtProperties;

    @Autowired
    private UserRepository userRepository;

    @Autowired
    private RefreshTokenRepository refreshTokenRepository;

    @Autowired
    private AuthHandoffRepository authHandoffRepository;

    @Autowired
    private LoginSessionRepository loginSessionRepository;

    @AfterEach
    void clear() {
        refreshTokenRepository.deleteAll();
        authHandoffRepository.deleteAll();
        loginSessionRepository.deleteAll();
        userRepository.deleteAll();
    }

    // ---------- 1단계: 로그인 시작 ----------

    @Test
    @DisplayName("로그인 시작은 소셜 인증으로 가는 주소를 돌려준다")
    void loginStartReturnsAuthorizeUrl() throws Exception {
        mockMvc.perform(post("/api/auth/login/start")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("""
                                {"provider":"GOOGLE","verifierHash":"hashed-verifier"}
                                """))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("LOGIN_STARTED"))
                .andExpect(jsonPath("$.data.authorizeUrl")
                        .value(org.hamcrest.Matchers.containsString("/oauth2/authorization/google")))
                .andExpect(jsonPath("$.data.authorizeUrl")
                        .value(org.hamcrest.Matchers.containsString("verifier_hash=hashed-verifier")));
    }

    @Test
    @DisplayName("verifierHash 가 없으면 로그인 시작을 거부한다")
    void loginStartRequiresVerifierHash() throws Exception {
        mockMvc.perform(post("/api/auth/login/start")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("""
                                {"provider":"GOOGLE"}
                                """))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"));
    }

    // ---------- 2단계: 인증 시작이 verifier 를 state 에 매어 두는가 ----------

    @Test
    @DisplayName("인증 시작은 제공자로 리다이렉트하면서 그 state 에 verifier 해시를 묶어 둔다")
    void authorizationBindsVerifierToState() throws Exception {
        MvcResult result = mockMvc.perform(
                        get("/oauth2/authorization/google").param("verifier_hash", "hashed-verifier"))
                .andExpect(status().is3xxRedirection())
                .andExpect(header().string("Location",
                        org.hamcrest.Matchers.containsString("accounts.google.com")))
                .andReturn();

        String state = queryParam(result.getResponse().getRedirectedUrl(), "state").orElseThrow();

        assertThat(loginSessionRepository.findById(state)).isPresent();
        assertThat(loginSessionRepository.findById(state).orElseThrow().getVerifierHash())
                .isEqualTo("hashed-verifier");
    }

    @Test
    @DisplayName("verifier 해시 없이 인증을 시작하면 세션을 만들지 않는다 — 인계 단계에서 막힌다")
    void authorizationWithoutVerifierCreatesNoSession() throws Exception {
        mockMvc.perform(get("/oauth2/authorization/google"))
                .andExpect(status().is3xxRedirection());

        assertThat(loginSessionRepository.count()).isZero();
    }

    // ---------- 3단계: 인계 코드 교환 ----------

    @Test
    @DisplayName("인계 코드를 교환하면 토큰과 친구 코드를 받는다 — 내부 PK 는 나가지 않는다")
    void exchangeReturnsTokens() throws Exception {
        User user = registerUser("g-flow-1");
        String handoff = handoffService.issue(user.getId(), TokenHasher.sha256(VERIFIER));

        mockMvc.perform(post("/api/auth/exchange")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(exchangeBody(handoff, VERIFIER)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("LOGIN_SUCCESS"))
                .andExpect(jsonPath("$.data.accessToken").isNotEmpty())
                .andExpect(jsonPath("$.data.refreshToken").isNotEmpty())
                .andExpect(jsonPath("$.data.friendCode").value(user.getFriendCode()))
                .andExpect(jsonPath("$.data.userId").doesNotExist());
    }

    @Test
    @DisplayName("verifier 가 틀리면 교환을 거부한다")
    void exchangeRejectsWrongVerifier() throws Exception {
        User user = registerUser("g-flow-2");
        String handoff = handoffService.issue(user.getId(), TokenHasher.sha256(VERIFIER));

        mockMvc.perform(post("/api/auth/exchange")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(exchangeBody(handoff, "wrong-verifier")))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("A008"));
    }

    // ---------- 4단계: 발급된 토큰으로 보호 API ----------

    @Test
    @DisplayName("발급받은 Access Token 으로 내 프로필을 조회할 수 있다")
    void accessTokenOpensProtectedApi() throws Exception {
        User user = registerUser("g-flow-3");
        String accessToken = exchangeForAccessToken(user);

        mockMvc.perform(get("/api/users/me").header("Authorization", "Bearer " + accessToken))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.friendCode").value(user.getFriendCode()))
                .andExpect(jsonPath("$.data.nickname").value(user.getNickname()));
    }

    @Test
    @DisplayName("토큰 없이 보호 API 를 부르면 A001 이다")
    void rejectsMissingToken() throws Exception {
        mockMvc.perform(get("/api/users/me"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("A001"));
    }

    @Test
    @DisplayName("위조된 토큰은 A003 이다 — 클라이언트는 재로그인해야 한다")
    void rejectsForgedToken() throws Exception {
        mockMvc.perform(get("/api/users/me").header("Authorization", "Bearer not-a-jwt"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("A003"));
    }

    @Test
    @DisplayName("만료된 토큰은 A004 로 구분된다 — 클라이언트는 조용히 재발급하면 된다")
    void reportsExpiredTokenDistinctly() throws Exception {
        User user = registerUser("g-flow-4");
        JwtProvider expiredIssuer = new JwtProvider(
                new JwtProperties(jwtProperties.secret(), -1_000L, -1_000L));
        String expired = expiredIssuer.createAccessToken(user.getFriendCode());

        mockMvc.perform(get("/api/users/me").header("Authorization", "Bearer " + expired))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("A004"));
    }

    @Test
    @DisplayName("Refresh Token 을 Bearer 로 쓰면 거부한다")
    void rejectsRefreshTokenAsBearer() throws Exception {
        User user = registerUser("g-flow-5");
        String refreshToken = exchangeFor(user, "$.data.refreshToken");

        mockMvc.perform(get("/api/users/me").header("Authorization", "Bearer " + refreshToken))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("A003"));
    }

    // ---------- 5단계: 재발급과 재사용 감지 ----------

    @Test
    @DisplayName("재발급하면 새 Access/Refresh 를 함께 준다")
    void reissueRotatesTokens() throws Exception {
        User user = registerUser("g-flow-6");
        String refreshToken = exchangeFor(user, "$.data.refreshToken");

        mockMvc.perform(post("/api/auth/reissue")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(reissueBody(refreshToken)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("TOKEN_REISSUED"))
                .andExpect(jsonPath("$.data.refreshToken")
                        .value(org.hamcrest.Matchers.not(refreshToken)));
    }

    @Test
    @DisplayName("한 번 쓴 Refresh Token 을 다시 쓰면 A005 로 막는다")
    void rejectsReusedRefreshToken() throws Exception {
        User user = registerUser("g-flow-7");
        String refreshToken = exchangeFor(user, "$.data.refreshToken");

        mockMvc.perform(post("/api/auth/reissue")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(reissueBody(refreshToken)))
                .andExpect(status().isOk());

        mockMvc.perform(post("/api/auth/reissue")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(reissueBody(refreshToken)))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("A005"));
    }

    @Test
    @DisplayName("로그아웃하면 그 뒤의 재발급이 막힌다")
    void logoutStopsReissue() throws Exception {
        User user = registerUser("g-flow-8");
        String accessToken = exchangeForAccessToken(user);
        String refreshToken = exchangeFor(user, "$.data.refreshToken");

        mockMvc.perform(post("/api/auth/logout").header("Authorization", "Bearer " + accessToken))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("LOGOUT_SUCCESS"));

        mockMvc.perform(post("/api/auth/reissue")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(reissueBody(refreshToken)))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("A005"));
    }

    // ---------- 도우미 ----------

    private User registerUser(String providerUserId) {
        return userRegistrationService.findOrRegister(AuthProvider.GOOGLE, providerUserId, "잠깨비");
    }

    private String exchangeForAccessToken(User user) throws Exception {
        return exchangeFor(user, "$.data.accessToken");
    }

    /** 인계 코드를 발급해 교환하고, 응답에서 원하는 토큰을 꺼낸다. */
    private String exchangeFor(User user, String jsonPathExpression) throws Exception {
        String handoff = handoffService.issue(user.getId(), TokenHasher.sha256(VERIFIER));
        MvcResult result = mockMvc.perform(post("/api/auth/exchange")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(exchangeBody(handoff, VERIFIER)))
                .andExpect(status().isOk())
                .andReturn();
        return com.jayway.jsonpath.JsonPath.read(
                result.getResponse().getContentAsString(StandardCharsets.UTF_8), jsonPathExpression);
    }

    private String exchangeBody(String handoff, String verifier) {
        return """
                {"handoff":"%s","verifier":"%s","deviceLabel":"test"}
                """.formatted(handoff, verifier);
    }

    private String reissueBody(String refreshToken) {
        return """
                {"refreshToken":"%s","deviceLabel":"test"}
                """.formatted(refreshToken);
    }

    private Optional<String> queryParam(String url, String name) {
        String query = URI.create(url).getQuery();
        return Arrays.stream(query.split("&"))
                .filter(pair -> pair.startsWith(name + "="))
                .map(pair -> pair.substring(name.length() + 1))
                .findFirst();
    }
}
