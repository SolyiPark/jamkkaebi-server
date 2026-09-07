package com.example.jamkkaebi.auth.service;

import com.example.jamkkaebi.auth.repository.AuthHandoffRepository;
import com.example.jamkkaebi.common.policy.TokenHasher;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 인계 코드가 <b>정말로 일회용인지</b>, verifier 없이는 못 쓰는지 검증한다.
 *
 * <p>딥링크는 가로채일 수 있다는 전제로 설계했으므로, 여기가 뚫리면 인계 코드를 두 단계로 나눈
 * 의미가 사라진다.
 *
 * <p>서비스가 스스로 커밋하므로 테스트에 {@code @Transactional} 을 걸지 않는다 — 걸면 조건부
 * UPDATE 의 동시성 성질을 검증하지 못하고, 롤백에 가려 실제 저장 결과도 보지 못한다.
 */
@SpringBootTest
class HandoffServiceTest {

    private static final String VERIFIER = "unity-random-verifier-abcdef";
    private static final Long USER_ID = 42L;

    @Autowired
    private HandoffService handoffService;

    @Autowired
    private AuthHandoffRepository authHandoffRepository;

    @AfterEach
    void clear() {
        authHandoffRepository.deleteAll();
    }

    @Test
    @DisplayName("올바른 verifier 로 교환하면 사용자 식별자를 돌려준다")
    void exchangesWithCorrectVerifier() {
        String code = handoffService.issue(USER_ID, TokenHasher.sha256(VERIFIER));

        assertThat(handoffService.exchange(code, VERIFIER)).isEqualTo(USER_ID);
    }

    @Test
    @DisplayName("같은 코드를 두 번 교환할 수 없다 — 일회용")
    void rejectsSecondExchange() {
        String code = handoffService.issue(USER_ID, TokenHasher.sha256(VERIFIER));
        handoffService.exchange(code, VERIFIER);

        assertThatThrownBy(() -> handoffService.exchange(code, VERIFIER))
                .isInstanceOf(BusinessException.class)
                .extracting(e -> ((BusinessException) e).getErrorCode())
                .isEqualTo(ErrorCode.INVALID_HANDOFF_CODE);
    }

    @Test
    @DisplayName("딥링크만 가로챈 쪽은 교환하지 못한다 — verifier 원문이 없기 때문")
    void rejectsWrongVerifier() {
        String code = handoffService.issue(USER_ID, TokenHasher.sha256(VERIFIER));

        assertThatThrownBy(() -> handoffService.exchange(code, "guessed-verifier"))
                .isInstanceOf(BusinessException.class)
                .extracting(e -> ((BusinessException) e).getErrorCode())
                .isEqualTo(ErrorCode.INVALID_HANDOFF_CODE);
    }

    @Test
    @DisplayName("verifier 를 틀리면 코드도 함께 소진된다 — 같은 코드로 계속 찍어 볼 수 없다")
    void burnsCodeEvenOnWrongVerifier() {
        String code = handoffService.issue(USER_ID, TokenHasher.sha256(VERIFIER));

        assertThatThrownBy(() -> handoffService.exchange(code, "guessed-verifier"))
                .isInstanceOf(BusinessException.class);

        // 이제는 올바른 verifier 를 가져와도 통과하지 못한다.
        assertThatThrownBy(() -> handoffService.exchange(code, VERIFIER))
                .isInstanceOf(BusinessException.class)
                .extracting(e -> ((BusinessException) e).getErrorCode())
                .isEqualTo(ErrorCode.INVALID_HANDOFF_CODE);
    }

    @Test
    @DisplayName("존재하지 않는 코드도 같은 오류로 돌려준다 — 코드 존재 여부를 알려주지 않는다")
    void rejectsUnknownCode() {
        assertThatThrownBy(() -> handoffService.exchange("made-up-code", VERIFIER))
                .isInstanceOf(BusinessException.class)
                .extracting(e -> ((BusinessException) e).getErrorCode())
                .isEqualTo(ErrorCode.INVALID_HANDOFF_CODE);
    }

    @Test
    @DisplayName("발급된 코드는 추측할 수 없을 만큼 길다")
    void issuesUnguessableCode() {
        String first = handoffService.issue(USER_ID, TokenHasher.sha256(VERIFIER));
        String second = handoffService.issue(USER_ID, TokenHasher.sha256(VERIFIER));

        assertThat(first).isNotEqualTo(second);
        assertThat(first.length()).isGreaterThanOrEqualTo(40); // 32바이트 base64url
    }
}
