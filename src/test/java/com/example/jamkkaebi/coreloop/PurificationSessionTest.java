package com.example.jamkkaebi.coreloop;

import com.example.jamkkaebi.purification.domain.SessionStatus;
import com.example.jamkkaebi.purification.service.ShapeSizePlanner;
import com.example.jamkkaebi.relic.domain.Difficulty;
import com.example.jamkkaebi.relic.domain.UserRelic;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.test.web.servlet.ResultActions;

import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.Callable;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * 정화 세션 — 발급 규칙과 결과 검증.
 *
 * <p>서버가 보드를 재현하지 않기로 했으므로(백엔드 계획 결정 ①), 여기서 지키는 것은 "발급한 세션인가,
 * 이미 정산했는가, 상한을 넘지 않았는가, 비상식적으로 빠르지 않은가" 네 가지뿐이다. 그 네 가지가
 * 실제로 걸리는지가 이 파일의 내용이다.
 */
class PurificationSessionTest extends CoreLoopApiTestSupport {

    private static final String SUCCESS_BODY = """
            {"result":"SUCCESS","tapsUsed":10,"scoutsUsed":1,"damage":0,
             "bonusTiles":{"jeongseong":0,"eraCrystal":0}}
            """;

    @Autowired
    private ShapeSizePlanner shapeSizePlanner;

    @Test
    @DisplayName("난이도 목록은 네 단계를 수치와 함께 준다")
    void 난이도_목록() throws Exception {
        Player player = player("탐색가");

        get(player, "/api/difficulties")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.difficulties.length()").value(4))
                .andExpect(jsonPath("$.data.difficulties[2].difficulty").value("NORMAL"))
                .andExpect(jsonPath("$.data.difficulties[2].boardWidth").value(7))
                .andExpect(jsonPath("$.data.difficulties[2].boardHeight").value(8))
                .andExpect(jsonPath("$.data.difficulties[2].tapBudget").value(19))
                .andExpect(jsonPath("$.data.difficulties[3].unlockStage").value("AWAKENED_PLUS"))
                // 기본 개방인 난이도에는 해금 조건 키가 실리지 않는다.
                .andExpect(jsonPath("$.data.difficulties[0].unlockStage").doesNotExist());
    }

    @Test
    @DisplayName("난이도마다 목표 형상을 개수와 칸 수 합으로 준다 — 기획서 6-11 의 3개/6칸 · 3개/8칸 · 3개/10칸 · 4개/14칸")
    void 난이도별_목표_형상() throws Exception {
        Player player = player("탐색가");

        get(player, "/api/difficulties")
                .andExpect(jsonPath("$.data.difficulties[0].shapeCount").value(3))
                .andExpect(jsonPath("$.data.difficulties[0].shapeCellTotal").value(6))
                .andExpect(jsonPath("$.data.difficulties[1].shapeCount").value(3))
                .andExpect(jsonPath("$.data.difficulties[1].shapeCellTotal").value(8))
                .andExpect(jsonPath("$.data.difficulties[2].shapeCount").value(3))
                .andExpect(jsonPath("$.data.difficulties[2].shapeCellTotal").value(10))
                .andExpect(jsonPath("$.data.difficulties[3].shapeCount").value(4))
                .andExpect(jsonPath("$.data.difficulties[3].shapeCellTotal").value(14));
    }

    @Test
    @DisplayName("보드의 형상별 칸 수는 그 판의 시드로 정해지고, 개수와 합은 난이도를 따른다")
    void 보드의_형상별_칸_수() throws Exception {
        Player player = player("발굴꾼");
        giveCompleted(player, 1);

        ResultActions started = post(player, "/api/purifications", """
                {"relicId":1,"difficulty":"EASY"}
                """)
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.data.board.shapeCount").value(3))
                .andExpect(jsonPath("$.data.board.shapeCellTotal").value(8))
                .andExpect(jsonPath("$.data.board.shapeSizes.length()").value(3));

        long seed = ((Number) read(started, "$.data.board.seed")).longValue();
        List<Integer> sizes = read(started, "$.data.board.shapeSizes");
        assertThat(sizes.stream().mapToInt(Integer::intValue).sum()).isEqualTo(8);
        // 서버는 배분을 저장하지 않는다 — 시드만 있으면 같은 배분을 다시 낼 수 있어야 한다.
        assertThat(sizes).isEqualTo(shapeSizePlanner.plan(seed, 3, 8));
    }

    @Test
    @DisplayName("세션에는 그 유물의 봉인 매개체가 실려 보드 아트 세트를 고를 수 있다")
    void 봉인_매개체() throws Exception {
        Player player = player("수집가");
        give(player, 1);
        give(player, 2);
        give(player, 6);

        post(player, "/api/purifications", "{\"relicId\":1}")
                .andExpect(jsonPath("$.data.sealMedium").value("RUST"));
        post(player, "/api/purifications", "{\"relicId\":2}")
                .andExpect(jsonPath("$.data.sealMedium").value("SOOT"));
        post(player, "/api/purifications", "{\"relicId\":6}")
                .andExpect(jsonPath("$.data.sealMedium").value("FOG"));
    }

    @Test
    @DisplayName("최초 정화는 보통 고정이고, 난이도를 보내면 거절한다")
    void 최초_정화는_난이도를_고를_수_없다() throws Exception {
        Player player = player("초보");
        give(player, 1);

        post(player, "/api/purifications", """
                {"relicId":1,"difficulty":"EASY"}
                """)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"))
                .andExpect(jsonPath("$.errors[0].field").value("difficulty"));

        post(player, "/api/purifications", """
                {"relicId":1}
                """)
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.data.entryRoute").value("FIRST"))
                .andExpect(jsonPath("$.data.phase").value("UNSEAL"))
                .andExpect(jsonPath("$.data.difficulty").value("NORMAL"))
                .andExpect(jsonPath("$.data.board.seed").isNumber())
                .andExpect(jsonPath("$.data.board.tapBudget").value(19))
                // 완벽 정화 보너스는 관리 플레이에서만 붙는다.
                .andExpect(jsonPath("$.data.perfectBonusEligible").value(false));
    }

    @Test
    @DisplayName("각성+ 전에는 어려움 난이도를 고를 수 없다")
    void 잠긴_난이도() throws Exception {
        Player player = player("욕심쟁이");
        giveCompleted(player, 1);

        post(player, "/api/purifications", """
                {"relicId":1,"difficulty":"HARD"}
                """)
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("P001"));
    }

    @Test
    @DisplayName("관리 플레이는 난이도를 보내야 한다")
    void 관리_플레이는_난이도가_필수() throws Exception {
        Player player = player("건망증");
        giveCompleted(player, 1);

        post(player, "/api/purifications", """
                {"relicId":1}
                """)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.errors[0].field").value("difficulty"));
    }

    @Test
    @DisplayName("탭 예산을 넘겨 보고하면 거절한다")
    void 탭_예산_초과() throws Exception {
        Player player = player("수상한사람");
        give(player, 1);
        String token = startPurification(player, 1, null);
        clock.advance(Duration.ofMinutes(1));

        post(player, "/api/purifications/{token}/result", """
                {"result":"SUCCESS","tapsUsed":999,"scoutsUsed":1,"damage":0,
                 "bonusTiles":{"jeongseong":0,"eraCrystal":0}}
                """, token)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("P003"));
    }

    @Test
    @DisplayName("세션을 받자마자 결과를 보내면 거절한다")
    void 너무_빠른_보고() throws Exception {
        Player player = player("자동화");
        give(player, 1);
        String token = startPurification(player, 1, null);

        post(player, "/api/purifications/{token}/result", SUCCESS_BODY, token)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("P003"));
    }

    @Test
    @DisplayName("새 세션을 시작하면 보고하지 않은 이전 세션은 무효가 된다")
    void 세션_쌓아두기_방지() throws Exception {
        Player player = player("저장왕");
        give(player, 1);
        String stale = startPurification(player, 1, null);
        startPurification(player, 1, null);
        clock.advance(Duration.ofMinutes(1));

        post(player, "/api/purifications/{token}/result", SUCCESS_BODY, stale)
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("P002"));
    }

    @Test
    @DisplayName("만료된 세션에는 보고할 수 없다")
    void 만료된_세션() throws Exception {
        Player player = player("느림보");
        give(player, 1);
        String token = startPurification(player, 1, null);
        clock.advance(Duration.ofHours(1));

        post(player, "/api/purifications/{token}/result", SUCCESS_BODY, token)
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("P002"));
    }

    @Test
    @DisplayName("남의 세션 토큰으로는 보고할 수 없다")
    void 남의_세션() throws Exception {
        Player owner = player("주인");
        Player stranger = player("나그네");
        give(owner, 1);
        String token = startPurification(owner, 1, null);
        clock.advance(Duration.ofMinutes(1));

        post(stranger, "/api/purifications/{token}/result", SUCCESS_BODY, token)
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("P002"));
    }

    @Test
    @DisplayName("같은 내용으로 다시 보고하면 처음 정산을 그대로 돌려주고 재화를 두 번 주지 않는다")
    void 정산_멱등() throws Exception {
        Player player = player("재시도");
        give(player, 1);
        String token = startPurification(player, 1, null);
        clock.advance(Duration.ofMinutes(1));

        post(player, "/api/purifications/{token}/result", SUCCESS_BODY, token)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.rewards.jeongseong").value(36));
        int afterFirst = jeongseongOf(player);

        post(player, "/api/purifications/{token}/result", SUCCESS_BODY, token)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.rewards.jeongseong").value(36));

        assertThat(jeongseongOf(player)).isEqualTo(afterFirst);
        assertThat(userRelicRepository.findByUserIdAndRelicId(player.id(), 1)
                .orElseThrow().getCurrentPhase().order()).isEqualTo(2);
    }

    @Test
    @DisplayName("정산한 세션에 다른 내용으로 보고하면 충돌이다")
    void 정산_후_다른_내용() throws Exception {
        Player player = player("변덕");
        give(player, 1);
        String token = startPurification(player, 1, null);
        clock.advance(Duration.ofMinutes(1));
        post(player, "/api/purifications/{token}/result", SUCCESS_BODY, token).andExpect(status().isOk());

        post(player, "/api/purifications/{token}/result", """
                {"result":"SUCCESS","tapsUsed":3,"scoutsUsed":0,"damage":0,
                 "bonusTiles":{"jeongseong":0,"eraCrystal":0}}
                """, token)
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("C003"));
    }

    @Test
    @DisplayName("실패해도 잃는 것이 없다 — 진행도와 선명도는 그대로고 다시 도전할 수 있다")
    void 실패는_잃는_것이_없다() throws Exception {
        Player player = player("도전자");
        give(player, 1);
        String token = startPurification(player, 1, null);
        clock.advance(Duration.ofMinutes(1));

        post(player, "/api/purifications/{token}/result", """
                {"result":"FAILURE","failReason":"DAMAGE_FULL","tapsUsed":15,"scoutsUsed":3,"damage":100,
                 "bonusTiles":{"jeongseong":0,"eraCrystal":0}}
                """, token)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.next.action").value("RETRY"))
                .andExpect(jsonPath("$.data.rewards.jeongseong").value(0))
                .andExpect(jsonPath("$.data.revealed").doesNotExist());

        UserRelic stored = userRelicRepository
                .findByUserIdAndRelicId(player.id(), 1).orElseThrow();
        assertThat(stored.getCurrentPhase().order()).isEqualTo(1);
        assertThat(jeongseongOf(player)).isZero();

        // 재도전에 비용이 없다.
        clearPhase(player, 1, null).andExpect(status().isOk());
    }

    @Test
    @DisplayName("성공에 실패 사유를 실으면 형식 오류다")
    void 결과와_사유의_조합() throws Exception {
        Player player = player("혼란");
        give(player, 1);
        String token = startPurification(player, 1, null);
        clock.advance(Duration.ofMinutes(1));

        post(player, "/api/purifications/{token}/result", """
                {"result":"SUCCESS","failReason":"GAVE_UP","tapsUsed":10,"scoutsUsed":1,"damage":0,
                 "bonusTiles":{"jeongseong":0,"eraCrystal":0}}
                """, token)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.errors[0].field").value("failReason"));

        post(player, "/api/purifications/{token}/result", """
                {"result":"FAILURE","tapsUsed":10,"scoutsUsed":1,"damage":100,
                 "bonusTiles":{"jeongseong":0,"eraCrystal":0}}
                """, token)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.errors[0].field").value("failReason"));
    }

    @Test
    @DisplayName("완벽 정화 보너스는 관리 플레이에만 붙는다")
    void 완벽_정화_보너스() throws Exception {
        Player player = player("장인");
        giveCompleted(player, 1);

        // 보통 배율 1.2 에 완벽 보너스 50% → 30 × 1.2 × 1.5 = 54
        clearPhase(player, 1, Difficulty.NORMAL)
                .andExpect(jsonPath("$.data.perfect").value(true))
                .andExpect(jsonPath("$.data.perfectStreak").value(1))
                .andExpect(jsonPath("$.data.rewards.jeongseong").value(54));

        // 손상이 있으면 보너스가 빠지고 연속도 끊긴다 — 30 × 1.2 = 36
        String token = startPurification(player, 1, Difficulty.NORMAL);
        clock.advance(Duration.ofMinutes(1));
        post(player, "/api/purifications/{token}/result", """
                {"result":"SUCCESS","tapsUsed":10,"scoutsUsed":1,"damage":40,
                 "bonusTiles":{"jeongseong":0,"eraCrystal":0}}
                """, token)
                .andExpect(jsonPath("$.data.perfect").value(false))
                .andExpect(jsonPath("$.data.perfectStreak").value(0))
                .andExpect(jsonPath("$.data.rewards.jeongseong").value(36));
    }

    @Test
    @DisplayName("보너스 타일은 배율 없이 캐낸 만큼 준다")
    void 보너스_타일() throws Exception {
        Player player = player("곡괭이");
        give(player, 1);
        String token = startPurification(player, 1, null);
        clock.advance(Duration.ofMinutes(1));

        // 30 × 1.2 = 36 에 정성 타일 2개(×5) → 46, 결정 타일 1개 → 1
        post(player, "/api/purifications/{token}/result", """
                {"result":"SUCCESS","tapsUsed":10,"scoutsUsed":1,"damage":0,
                 "bonusTiles":{"jeongseong":2,"eraCrystal":1}}
                """, token)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.rewards.jeongseong").value(46))
                .andExpect(jsonPath("$.data.rewards.eraCrystal.amount").value(1));
    }

    @Test
    @DisplayName("같은 사용자의 정화 시작이 동시에 몰려도 보고할 수 있는 세션은 마지막 하나뿐이다")
    void 동시_시작() throws Exception {
        Player player = player("연타왕");
        give(player, 1);

        ExecutorService pool = Executors.newFixedThreadPool(4);
        try {
            for (int round = 0; round < 10; round++) {
                List<Future<Integer>> starts = new ArrayList<>();
                for (int i = 0; i < 4; i++) {
                    starts.add(pool.submit(() -> post(player, "/api/purifications", "{\"relicId\":1}")
                            .andReturn().getResponse().getStatus()));
                }
                for (Future<Integer> start : starts) {
                    assertThat(start.get(10, TimeUnit.SECONDS)).as("%d 번째 판의 응답", round).isEqualTo(201);
                }
                assertThat(playSessionRepository.findAllByUserIdAndStatus(player.id(), SessionStatus.ISSUED))
                        .as("%d 번째 판 뒤 보고할 수 있는 세션", round)
                        .hasSize(1);
            }
        } finally {
            pool.shutdownNow();
        }
    }

    @Test
    @DisplayName("정화 시작과 결과 보고는 모두 사용자 행을 먼저 잠근다 — 잠금 순서가 같아 서로 엇갈려 교착되지 않는다")
    void 시작과_보고는_사용자_행을_먼저_잠근다() throws Exception {
        Player player = player("줄서기");
        give(player, 1);
        String token = startPurification(player, 1, null);
        clock.advance(Duration.ofMinutes(1));

        assertWaitsForUserLock(player,
                () -> post(player, "/api/purifications/{token}/result", SUCCESS_BODY, token)
                        .andReturn().getResponse().getStatus(),
                200);
        assertWaitsForUserLock(player,
                () -> post(player, "/api/purifications", "{\"relicId\":1}")
                        .andReturn().getResponse().getStatus(),
                201);
    }

    /**
     * 다른 트랜잭션이 사용자 행을 잡고 있는 동안 {@code request} 가 끝나지 않고 기다렸다가, 잠금이 풀린 뒤에야
     * {@code expectedStatus} 로 끝나는지 본다.
     */
    private void assertWaitsForUserLock(Player player, Callable<Integer> request, int expectedStatus)
            throws Exception {
        CountDownLatch locked = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);
        ExecutorService pool = Executors.newFixedThreadPool(2);
        try {
            Future<?> holder = pool.submit(() -> transactionTemplate.executeWithoutResult(status -> {
                userRepository.lockAllByIds(List.of(player.id()));
                locked.countDown();
                try {
                    release.await(10, TimeUnit.SECONDS);
                } catch (InterruptedException interrupted) {
                    Thread.currentThread().interrupt();
                }
            }));
            assertThat(locked.await(10, TimeUnit.SECONDS)).isTrue();

            Future<Integer> waiting = pool.submit(request);
            assertThatThrownBy(() -> waiting.get(600, TimeUnit.MILLISECONDS)).isInstanceOf(TimeoutException.class);

            release.countDown();
            holder.get(10, TimeUnit.SECONDS);
            assertThat(waiting.get(10, TimeUnit.SECONDS)).isEqualTo(expectedStatus);
        } finally {
            release.countDown();
            pool.shutdownNow();
        }
    }
}
