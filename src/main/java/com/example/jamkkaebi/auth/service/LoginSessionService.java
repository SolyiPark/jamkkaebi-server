package com.example.jamkkaebi.auth.service;

import com.example.jamkkaebi.auth.config.AuthProperties;
import com.example.jamkkaebi.auth.domain.LoginSession;
import com.example.jamkkaebi.auth.repository.LoginSessionRepository;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDateTime;
import java.time.ZoneId;
import java.util.Optional;

/**
 * 소셜 인증이 도는 동안 {@code state} 에 verifier 해시를 매어 둔다.
 */
@Service
public class LoginSessionService {

    private static final ZoneId KST = ZoneId.of("Asia/Seoul");

    private final LoginSessionRepository loginSessionRepository;
    private final AuthProperties authProperties;

    public LoginSessionService(LoginSessionRepository loginSessionRepository,
                               AuthProperties authProperties) {
        this.loginSessionRepository = loginSessionRepository;
        this.authProperties = authProperties;
    }

    @Transactional
    public void start(String state, String verifierHash) {
        loginSessionRepository.save(LoginSession.builder()
                .state(state)
                .verifierHash(verifierHash)
                .expiresAt(LocalDateTime.now(KST).plus(authProperties.loginSessionTtl()))
                .build());
    }

    /**
     * 로그인이 끝난 {@code state} 의 verifier 해시를 꺼내고 세션을 지운다.
     *
     * <p>한 번 쓰면 지우는 이유는, 같은 state 로 두 번 인계 코드를 만들 여지를 남기지 않기
     * 위해서다. 만료된 세션도 없는 것으로 본다.
     *
     * @return verifier 해시. 세션이 없거나 만료됐으면 비어 있다.
     */
    @Transactional
    public Optional<String> consume(String state) {
        if (state == null || state.isBlank()) {
            return Optional.empty();
        }
        Optional<LoginSession> session = loginSessionRepository.findById(state);
        session.ifPresent(loginSessionRepository::delete);
        return session
                .filter(found -> !found.isExpired(LocalDateTime.now(KST)))
                .map(LoginSession::getVerifierHash);
    }

    /** 쓰이지 않은 채 만료된 세션을 치운다. */
    @Transactional
    public int purgeExpired() {
        return loginSessionRepository.deleteExpired(LocalDateTime.now(KST));
    }
}
