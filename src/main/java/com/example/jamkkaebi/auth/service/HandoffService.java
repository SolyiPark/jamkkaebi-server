package com.example.jamkkaebi.auth.service;

import com.example.jamkkaebi.auth.config.AuthProperties;
import com.example.jamkkaebi.auth.domain.AuthHandoff;
import com.example.jamkkaebi.auth.repository.AuthHandoffRepository;
import com.example.jamkkaebi.common.policy.TokenHasher;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import org.springframework.stereotype.Service;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.TransactionDefinition;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.transaction.support.TransactionTemplate;

import java.security.SecureRandom;
import java.time.LocalDateTime;
import java.time.ZoneId;
import java.util.Base64;

/**
 * 일회용 인계 코드를 발급하고 교환한다.
 */
@Service
public class HandoffService {

    private static final ZoneId KST = ZoneId.of("Asia/Seoul");
    private static final int CODE_BYTES = 32;

    private final AuthHandoffRepository authHandoffRepository;
    private final AuthProperties authProperties;
    private final TransactionTemplate consumeTransaction;
    private final SecureRandom random = new SecureRandom();

    public HandoffService(AuthHandoffRepository authHandoffRepository,
                          AuthProperties authProperties,
                          PlatformTransactionManager transactionManager) {
        this.authHandoffRepository = authHandoffRepository;
        this.authProperties = authProperties;

        // 코드 소진은 교환의 성공 여부와 무관하게 확정돼야 한다. 바깥 트랜잭션에 얹으면 verifier
        // 불일치로 예외를 던지는 순간 소진까지 함께 롤백돼, 같은 코드로 verifier 만 바꿔 가며
        // 무한히 시도할 수 있게 된다. 그래서 소진만 별도 트랜잭션으로 끊어 커밋한다.
        this.consumeTransaction = new TransactionTemplate(transactionManager);
        this.consumeTransaction.setPropagationBehavior(
                TransactionDefinition.PROPAGATION_REQUIRES_NEW);
    }

    /**
     * 로그인을 마친 사용자에게 인계 코드를 발급한다.
     *
     * @param verifierHash 로그인 세션에서 옮겨 온 값. 교환 때 이 해시와 대조한다.
     */
    @Transactional
    public String issue(Long userId, String verifierHash) {
        byte[] bytes = new byte[CODE_BYTES];
        random.nextBytes(bytes);
        String code = Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);

        authHandoffRepository.save(AuthHandoff.builder()
                .code(code)
                .userId(userId)
                .verifierHash(verifierHash)
                .expiresAt(LocalDateTime.now(KST).plus(authProperties.handoffTtl()))
                .build());
        return code;
    }

    /**
     * 인계 코드를 사용자 식별자로 바꾼다. 코드는 <b>제시된 순간</b> 폐기된다.
     *
     * <p>순서가 중요하다. 먼저 코드를 소진하고(조건부 UPDATE — 동시에 두 번 교환해도 하나만 통과),
     * 그 다음 {@code verifier} 를 대조한다. 반대로 하면 딥링크를 가로챈 쪽이 같은 코드로 verifier 를
     * 계속 바꿔 가며 시도할 수 있다.
     *
     * <p>없음·만료·이미 사용·verifier 불일치를 모두 같은 오류로 돌려준다. (찍어맞추기 방지)
     */
    public Long exchange(String code, String verifier) {
        AuthHandoff handoff = consume(code);
        if (handoff == null || !TokenHasher.matches(verifier, handoff.getVerifierHash())) {
            throw new BusinessException(ErrorCode.INVALID_HANDOFF_CODE);
        }
        return handoff.getUserId();
    }

    /** 코드를 소진하고 그 내용을 돌려준다. 쓸 수 없는 코드면 {@code null}. */
    private AuthHandoff consume(String code) {
        return consumeTransaction.execute(status -> {
            LocalDateTime now = LocalDateTime.now(KST);
            if (authHandoffRepository.consume(code, now) != 1) {
                return null;
            }
            return authHandoffRepository.findById(code).orElse(null);
        });
    }

    /** 다 쓴 인계 코드와 만료된 코드를 치운다. */
    @Transactional
    public int purgeExpired() {
        return authHandoffRepository.deleteExpired(LocalDateTime.now(KST));
    }
}
