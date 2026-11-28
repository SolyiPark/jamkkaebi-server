package com.example.jamkkaebi.relic.service;

import com.example.jamkkaebi.relic.config.ClarityProperties;
import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Phase;
import com.example.jamkkaebi.relic.domain.UserRelic;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Nested;
import org.junit.jupiter.api.Test;

import java.time.LocalDateTime;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 선명도와 관리 플레이로 한 칸씩 오르는 규칙.
 *
 * <p>DB 없이 도는 순수 계산이라 시각만 옮기면 14일 뒤를 그 자리에서 확인할 수 있다. 실제로 기다려서
 * 확인할 수 없는 규칙이라 여기에 테스트를 몰아 둔다 (백엔드 계획 결정 ④).
 */
class ClarityPolicyTest {

    private static final LocalDateTime PURIFIED_AT = LocalDateTime.of(2026, 9, 1, 12, 0);

    private final ClarityPolicy policy =
            new ClarityPolicy(new ClarityProperties(null, null, null));

    @Nested
    @DisplayName("선명도")
    class 선명도 {

        @Test
        @DisplayName("7일 미만이면 선명, 7일부터 흐려짐, 14일부터 재봉인")
        void 경계값() {
            UserRelic spirit = completed();

            assertThat(policy.clarityOf(spirit, PURIFIED_AT.plusDays(7).minusMinutes(1)))
                    .isEqualTo(Clarity.CLEAR);
            assertThat(policy.clarityOf(spirit, PURIFIED_AT.plusDays(7))).isEqualTo(Clarity.FADED);
            assertThat(policy.clarityOf(spirit, PURIFIED_AT.plusDays(14).minusMinutes(1)))
                    .isEqualTo(Clarity.FADED);
            assertThat(policy.clarityOf(spirit, PURIFIED_AT.plusDays(14))).isEqualTo(Clarity.RESEALED);
        }

        @Test
        @DisplayName("아직 오염 관리를 마치지 않은 유물에는 선명도가 없다")
        void 미완료_유물() {
            UserRelic inProgress = UserRelic.acquired(1L, 1, PURIFIED_AT);

            assertThat(policy.clarityOf(inProgress, PURIFIED_AT.plusDays(30))).isNull();
        }

        @Test
        @DisplayName("재봉인된 뒤에는 예고할 시각이 없다")
        void 재봉인_예고_시각() {
            UserRelic spirit = completed();

            assertThat(policy.resealAt(spirit, PURIFIED_AT.plusDays(1)))
                    .isEqualTo(PURIFIED_AT.plusDays(14));
            assertThat(policy.resealAt(spirit, PURIFIED_AT.plusDays(20))).isNull();
        }
    }

    @Nested
    @DisplayName("다음에 선명도가 바뀌는 시각")
    class 다음_변화_시각 {

        @Test
        @DisplayName("선명이면 흐려지는 시각, 흐려짐이면 재봉인되는 시각, 재봉인이면 없다")
        void 선명도별() {
            UserRelic spirit = completed();

            assertThat(policy.nextChangeAt(spirit, PURIFIED_AT.plusDays(1))).isEqualTo(PURIFIED_AT.plusDays(7));
            assertThat(policy.nextChangeAt(spirit, PURIFIED_AT.plusDays(8))).isEqualTo(PURIFIED_AT.plusDays(14));
            assertThat(policy.nextChangeAt(spirit, PURIFIED_AT.plusDays(14))).isNull();
        }

        @Test
        @DisplayName("오염 관리를 마치지 않은 유물에는 없다")
        void 미완료_유물() {
            assertThat(policy.nextChangeAt(UserRelic.acquired(1L, 1, PURIFIED_AT), PURIFIED_AT)).isNull();
        }
    }

    @Nested
    @DisplayName("기준 시각과 남은 순서")
    class 기준_시각 {

        @Test
        @DisplayName("선명은 지금, 흐려짐은 지금 - 7일. 재봉인으로는 오를 수 없다")
        void 오른_선명도의_기준_시각() {
            LocalDateTime now = PURIFIED_AT.plusDays(20);

            assertThat(policy.anchorFor(Clarity.CLEAR, now)).isEqualTo(now);
            assertThat(policy.anchorFor(Clarity.FADED, now)).isEqualTo(now.minusDays(7));
            assertThatThrownBy(() -> policy.anchorFor(Clarity.RESEALED, now))
                    .isInstanceOf(IllegalArgumentException.class);
        }

        @Test
        @DisplayName("선명까지 남은 순서 — 재봉인은 봉인 해제·기억 회복, 흐려짐은 기억 회복, 선명은 오염 관리")
        void 남은_순서() {
            assertThat(policy.remainingPhases(Clarity.RESEALED)).containsExactly(Phase.UNSEAL, Phase.RECALL);
            assertThat(policy.remainingPhases(Clarity.FADED)).containsExactly(Phase.RECALL);
            assertThat(policy.remainingPhases(Clarity.CLEAR)).containsExactly(Phase.GUARD);
        }

        @Test
        @DisplayName("선명도는 한 칸씩 오르고 선명은 그대로다")
        void 한_칸_오르기() {
            assertThat(Clarity.RESEALED.stepUp()).isEqualTo(Clarity.FADED);
            assertThat(Clarity.FADED.stepUp()).isEqualTo(Clarity.CLEAR);
            assertThat(Clarity.CLEAR.stepUp()).isEqualTo(Clarity.CLEAR);
        }
    }

    @Nested
    @DisplayName("관리 플레이 기록")
    class 관리_플레이_기록 {

        @Test
        @DisplayName("재봉인에서 흐려짐으로 오르면 7일 뒤 재봉인되고, 마지막 정화 시각은 지금이 된다")
        void 재봉인에서_흐려짐으로() {
            UserRelic spirit = completed();
            LocalDateTime now = PURIFIED_AT.plusDays(20);
            assertThat(policy.clarityOf(spirit, now)).isEqualTo(Clarity.RESEALED);

            spirit.clearCarePlay(policy.anchorFor(Clarity.FADED, now), now);

            assertThat(policy.clarityOf(spirit, now)).isEqualTo(Clarity.FADED);
            assertThat(policy.nextChangeAt(spirit, now)).isEqualTo(now.plusDays(7));
            assertThat(spirit.getLastPurifiedAt()).isEqualTo(now);
            assertThat(spirit.getClarityAnchorAt()).isEqualTo(now.minusDays(7));
            assertThat(spirit.getCareCount()).isEqualTo(1);
        }

        @Test
        @DisplayName("흐려짐에서 선명으로 오르면 7일 뒤 흐려진다")
        void 흐려짐에서_선명으로() {
            UserRelic spirit = completed();
            LocalDateTime now = PURIFIED_AT.plusDays(10);

            spirit.clearCarePlay(policy.anchorFor(Clarity.CLEAR, now), now);

            assertThat(policy.clarityOf(spirit, now)).isEqualTo(Clarity.CLEAR);
            assertThat(policy.nextChangeAt(spirit, now)).isEqualTo(now.plusDays(7));
        }

        @Test
        @DisplayName("서버 시각이 마지막 정화보다 앞서도 흐려짐으로 오른 정령이 선명으로 보이지 않는다")
        void 시각_역행() {
            UserRelic spirit = completed();
            LocalDateTime now = PURIFIED_AT.plusDays(20);
            spirit.clearCarePlay(policy.anchorFor(Clarity.FADED, now), now);

            assertThat(policy.clarityOf(spirit, now.minusSeconds(1))).isEqualTo(Clarity.FADED);
        }

        @Test
        @DisplayName("지금보다 늦은 기준 시각이나 미완료 유물은 거부한다")
        void 잘못된_기록() {
            UserRelic spirit = completed();
            LocalDateTime now = PURIFIED_AT.plusDays(1);
            assertThatThrownBy(() -> spirit.clearCarePlay(now.plusSeconds(1), now))
                    .isInstanceOf(IllegalArgumentException.class);
            assertThatThrownBy(() -> UserRelic.acquired(1L, 1, now).clearCarePlay(now, now))
                    .isInstanceOf(IllegalStateException.class);
            assertThat(spirit.getCareCount()).isZero();
        }

        @Test
        @DisplayName("최초 정화 완료는 두 시각을 모두 지금으로 두고, 돌봄 횟수를 세지 않는다")
        void 최초_정화_완료() {
            UserRelic spirit = completed();
            assertThat(spirit.getLastPurifiedAt()).isEqualTo(PURIFIED_AT);
            assertThat(spirit.getClarityAnchorAt()).isEqualTo(PURIFIED_AT);
            assertThat(spirit.getCareCount()).isZero();
        }
    }

    /** 오염 관리까지 마쳐 {@link #PURIFIED_AT} 에 선명해진 도깨비. */
    private static UserRelic completed() {
        UserRelic spirit = UserRelic.acquired(1L, 1, PURIFIED_AT.minusDays(1));
        spirit.clearFirstPlay(PURIFIED_AT);
        spirit.clearFirstPlay(PURIFIED_AT);
        spirit.clearFirstPlay(PURIFIED_AT);
        return spirit;
    }
}
