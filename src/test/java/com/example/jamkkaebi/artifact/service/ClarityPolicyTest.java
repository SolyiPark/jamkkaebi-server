package com.example.jamkkaebi.artifact.service;

import com.example.jamkkaebi.artifact.config.GrowthProperties;
import com.example.jamkkaebi.artifact.domain.Clarity;
import com.example.jamkkaebi.artifact.domain.Phase;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Nested;
import org.junit.jupiter.api.Test;

import java.time.LocalDateTime;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 선명도와 복구 순서.
 *
 * <p>DB 없이 도는 순수 계산이라 시각만 옮기면 14일 뒤를 그 자리에서 확인할 수 있다. 실제로 기다려서
 * 확인할 수 없는 규칙이라 여기에 테스트를 몰아 둔다 (백엔드 계획 결정 ④).
 */
class ClarityPolicyTest {

    private static final LocalDateTime PURIFIED_AT = LocalDateTime.of(2026, 9, 1, 12, 0);

    private final ClarityPolicy policy =
            new ClarityPolicy(new GrowthProperties(null, null, null, null));

    @Nested
    @DisplayName("선명도")
    class 선명도 {

        @Test
        @DisplayName("7일 미만이면 선명, 7일부터 흐려짐, 14일부터 재봉인")
        void 경계값() {
            UserArtifact spirit = completed();

            assertThat(policy.clarityOf(spirit, PURIFIED_AT.plusDays(7).minusMinutes(1)))
                    .isEqualTo(Clarity.CLEAR);
            assertThat(policy.clarityOf(spirit, PURIFIED_AT.plusDays(7))).isEqualTo(Clarity.FADED);
            assertThat(policy.clarityOf(spirit, PURIFIED_AT.plusDays(14).minusMinutes(1)))
                    .isEqualTo(Clarity.FADED);
            assertThat(policy.clarityOf(spirit, PURIFIED_AT.plusDays(14))).isEqualTo(Clarity.RESEALED);
        }

        @Test
        @DisplayName("아직 정령 수호를 마치지 않은 유물에는 선명도가 없다")
        void 미완료_유물() {
            UserArtifact inProgress = UserArtifact.acquired(1L, 1, PURIFIED_AT);

            assertThat(policy.clarityOf(inProgress, PURIFIED_AT.plusDays(30))).isNull();
        }

        @Test
        @DisplayName("재봉인된 뒤에는 예고할 시각이 없다")
        void 재봉인_예고_시각() {
            UserArtifact spirit = completed();

            assertThat(policy.resealAt(spirit, PURIFIED_AT.plusDays(1)))
                    .isEqualTo(PURIFIED_AT.plusDays(14));
            assertThat(policy.resealAt(spirit, PURIFIED_AT.plusDays(20))).isNull();
        }
    }

    /**
     * API 명세 부록 C-6 의 표를 그대로 옮긴 것이다.
     *
     * <p>이 규칙이 없으면 복구를 절반 해둔 사람이 더 방치했을 때 처음부터 다시 하게 되고, 그러면
     * 중간에 손을 떼는 편이 이득이 된다.
     */
    @Nested
    @DisplayName("복구 순서 — 이미 클리어한 페이즈는 다시 시키지 않는다")
    class 복구_순서 {

        @Test
        @DisplayName("선명하면 정령 수호만 반복한다")
        void 선명() {
            assertThat(policy.remainingPhases(Clarity.CLEAR, null)).containsExactly(Phase.GUARD);
        }

        @Test
        @DisplayName("흐려진 뒤 아무것도 하지 않았으면 기억 회복부터")
        void 흐려짐_처음부터() {
            assertThat(policy.remainingPhases(Clarity.FADED, null))
                    .containsExactly(Phase.RECALL, Phase.GUARD);
        }

        @Test
        @DisplayName("흐려짐 복구로 기억 회복만 마쳤으면 정령 수호만 남는다")
        void 흐려짐_절반() {
            assertThat(policy.remainingPhases(Clarity.FADED, Phase.RECALL))
                    .containsExactly(Phase.GUARD);
        }

        @Test
        @DisplayName("그 상태로 방치해 재봉인돼도 봉인 해제를 다시 시키지 않는다")
        void 복구_도중_재봉인() {
            assertThat(policy.remainingPhases(Clarity.RESEALED, Phase.RECALL))
                    .containsExactly(Phase.GUARD);
        }

        @Test
        @DisplayName("아무것도 하지 않은 채 재봉인되면 봉인 해제부터 전부")
        void 재봉인_처음부터() {
            assertThat(policy.remainingPhases(Clarity.RESEALED, null))
                    .containsExactly(Phase.UNSEAL, Phase.RECALL, Phase.GUARD);
        }

        @Test
        @DisplayName("봉인 해제까지만 마치고 재봉인이 깊어져도 그 뒤만 남는다")
        void 재봉인_절반() {
            assertThat(policy.remainingPhases(Clarity.RESEALED, Phase.UNSEAL))
                    .containsExactly(Phase.RECALL, Phase.GUARD);
        }
    }

    @Nested
    @DisplayName("복구 진행도 기록")
    class 진행도_기록 {

        @Test
        @DisplayName("정령 수호를 마치면 선명으로 돌아오고 진행도가 비워진다")
        void 정령_수호_클리어() {
            UserArtifact spirit = completed();
            LocalDateTime later = PURIFIED_AT.plusDays(10);

            spirit.clearCarePhase(Phase.RECALL, later);
            assertThat(spirit.getRestorePhase()).isEqualTo(Phase.RECALL);
            assertThat(policy.clarityOf(spirit, later)).isEqualTo(Clarity.FADED);

            spirit.clearCarePhase(Phase.GUARD, later);
            assertThat(spirit.getRestorePhase()).isNull();
            assertThat(policy.clarityOf(spirit, later)).isEqualTo(Clarity.CLEAR);
        }
    }

    /** 정령 수호까지 마쳐 {@link #PURIFIED_AT} 에 선명해진 도깨비. */
    private static UserArtifact completed() {
        UserArtifact spirit = UserArtifact.acquired(1L, 1, PURIFIED_AT.minusDays(1));
        spirit.clearFirstPhase(PURIFIED_AT);
        spirit.clearFirstPhase(PURIFIED_AT);
        spirit.clearFirstPhase(PURIFIED_AT);
        return spirit;
    }
}
