package com.example.jamkkaebi.global.security;

import com.example.jamkkaebi.auth.handler.OAuth2LoginFailureHandler;
import com.example.jamkkaebi.auth.handler.OAuth2LoginSuccessHandler;
import com.example.jamkkaebi.auth.handler.VerifierBindingAuthorizationRequestResolver;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.core.annotation.Order;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.annotation.web.configurers.AbstractHttpConfigurer;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.authentication.UsernamePasswordAuthenticationFilter;

/**
 * 보안 설정. 체인이 <b>둘</b>이고, 나눈 이유가 이 파일의 핵심이다.
 *
 * <ul>
 *   <li><b>소셜 로그인 체인</b> — 브라우저가 오가는 구간. Spring Security 가 {@code state} 를
 *       세션에 담아 검증하므로 세션이 필요하다.</li>
 *   <li><b>게임 API 체인</b> — 게임 클라이언트가 JWT 로만 호출하는 구간. 완전히 무상태다.</li>
 * </ul>
 *
 * <p>하나로 합치면 둘 중 하나를 포기해야 한다. 전부 무상태로 두면 {@code state} 검증이 깨져 CSRF
 * 방어가 사라지고, 전부 세션을 쓰면 브라우저 쿠키만으로 게임 API 에 들어올 수 있는 두 번째 인증
 * 경로가 생긴다.
 */
@Configuration
public class SecurityConfig {

    private static final String[] PUBLIC_API_ENDPOINTS = {
            "/api/auth/login/start",
            "/api/auth/exchange",
            "/api/auth/reissue",
            "/dev/**", // dev 프로파일에서만 컨트롤러가 등록된다.
    };

    private final JwtAuthenticationFilter jwtAuthenticationFilter;
    private final RestAuthenticationEntryPoint authenticationEntryPoint;
    private final RestAccessDeniedHandler accessDeniedHandler;

    public SecurityConfig(JwtAuthenticationFilter jwtAuthenticationFilter,
                          RestAuthenticationEntryPoint authenticationEntryPoint,
                          RestAccessDeniedHandler accessDeniedHandler) {
        this.jwtAuthenticationFilter = jwtAuthenticationFilter;
        this.authenticationEntryPoint = authenticationEntryPoint;
        this.accessDeniedHandler = accessDeniedHandler;
    }

    /**
     * 소셜 로그인 구간. 인가 요청 시작({@code /oauth2/authorization/**})과 제공자 콜백
     * ({@code /login/oauth2/code/**})만 여기로 들어온다.
     */
    @Bean
    @Order(1)
    public SecurityFilterChain oauth2LoginFilterChain(
            HttpSecurity http,
            VerifierBindingAuthorizationRequestResolver authorizationRequestResolver,
            OAuth2LoginSuccessHandler successHandler,
            OAuth2LoginFailureHandler failureHandler) throws Exception {
        http
                .securityMatcher("/oauth2/**", "/login/oauth2/**")
                // 소셜에서 돌아오는 콜백은 리다이렉트다.
                .csrf(AbstractHttpConfigurer::disable)
                .authorizeHttpRequests(auth -> auth.anyRequest().permitAll())
                .oauth2Login(oauth2 -> oauth2
                        .authorizationEndpoint(endpoint -> endpoint
                                .authorizationRequestResolver(authorizationRequestResolver))
                        .successHandler(successHandler)
                        .failureHandler(failureHandler));
        return http.build();
    }

    /** 게임 API 구간. JWT 만 신뢰하고 세션은 만들지 않는다. */
    @Bean
    @Order(2)
    public SecurityFilterChain apiFilterChain(HttpSecurity http) throws Exception {
        http
                .csrf(AbstractHttpConfigurer::disable)
                // 클라이언트가 게임 앱(브라우저가 아님)이라 CORS 는 쓰지 않는다. 관리자용 웹을
                // 붙이게 되면 그때 설정을 추가한다.
                .cors(AbstractHttpConfigurer::disable)
                .formLogin(AbstractHttpConfigurer::disable)
                .httpBasic(AbstractHttpConfigurer::disable)
                .sessionManagement(session ->
                        session.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
                .authorizeHttpRequests(auth -> auth
                        .requestMatchers(PUBLIC_API_ENDPOINTS).permitAll()
                        .anyRequest().authenticated())
                .exceptionHandling(handler -> handler
                        .authenticationEntryPoint(authenticationEntryPoint)
                        .accessDeniedHandler(accessDeniedHandler))
                .addFilterBefore(jwtAuthenticationFilter, UsernamePasswordAuthenticationFilter.class);
        return http.build();
    }
}
