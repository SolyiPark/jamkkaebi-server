package com.example.jamkkaebi.purification.service;

import com.example.jamkkaebi.purification.config.PurificationProperties;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.HashSet;
import java.util.List;
import java.util.Random;
import java.util.Set;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 형상별 칸 수 배분 — 난이도의 (형상 개수, 칸 수 합) 을 판마다 시드로 나눈다.
 *
 * <p>골든 값은 보드 생성기 명세 §3·§5.4 를 그대로 옮긴 다른 언어 구현(Python)으로 뽑았다. 클라이언트(C#)와
 * 솔버도 같은 값을 내야 하므로, 여기가 깨지면 서버 쪽 구현이 명세에서 벗어났다는 뜻이다.
 */
class ShapeSizePlannerTest {

    private final ShapeSizePlanner planner = new ShapeSizePlanner(
            new PurificationProperties(null, null, null, null, null, null, null, null, null, null, null));

    @Test
    @DisplayName("SplitMix64 는 참조 구현과 같은 수를 낸다")
    void 난수_참조값() {
        SplitMix64 random = new SplitMix64(0);

        assertThat(random.next()).isEqualTo(0xE220A8397B1DCDAFL);
        assertThat(random.next()).isEqualTo(0x6E789E6AA1B965F4L);
        assertThat(random.next()).isEqualTo(0x06C45D188009454FL);
    }

    @Test
    @DisplayName("후보 배분은 내림차순 배열을 사전순 역순으로 나열한다")
    void 후보_목록() {
        assertThat(planner.candidates(3, 6)).containsExactly(List.of(2, 2, 2));
        assertThat(planner.candidates(3, 8)).containsExactly(List.of(4, 2, 2), List.of(3, 3, 2));
        assertThat(planner.candidates(3, 10)).containsExactly(
                List.of(5, 3, 2), List.of(4, 4, 2), List.of(4, 3, 3));
        assertThat(planner.candidates(4, 14)).containsExactly(
                List.of(5, 5, 2, 2), List.of(5, 4, 3, 2), List.of(5, 3, 3, 3),
                List.of(4, 4, 4, 2), List.of(4, 4, 3, 3));
    }

    @Test
    @DisplayName("같은 시드는 언제나 같은 배분이다 — 다른 언어 구현과 값까지 같다")
    void 골든_값() {
        assertThat(planner.plan(0L, 3, 10)).containsExactly(4, 3, 3);
        assertThat(planner.plan(0L, 4, 14)).containsExactly(5, 5, 2, 2);
        assertThat(planner.plan(0L, 3, 8)).containsExactly(4, 2, 2);
        assertThat(planner.plan(1L, 3, 10)).containsExactly(4, 4, 2);
        assertThat(planner.plan(1L, 4, 14)).containsExactly(4, 4, 4, 2);
        assertThat(planner.plan(42L, 3, 10)).containsExactly(5, 3, 2);
        assertThat(planner.plan(8123457712334551L, 3, 10)).containsExactly(4, 4, 2);
        assertThat(planner.plan(Long.MAX_VALUE, 4, 14)).containsExactly(5, 5, 2, 2);
    }

    @Test
    @DisplayName("어떤 시드든 개수·합·크기 범위를 지키고, 판이 쌓이면 모든 배분이 나온다")
    void 배분_불변식() {
        Random seeds = new Random(7);
        Set<List<Integer>> seen = new HashSet<>();
        for (int i = 0; i < 500; i++) {
            long seed = seeds.nextLong() & Long.MAX_VALUE;
            List<Integer> sizes = planner.plan(seed, 4, 14);

            assertThat(sizes).hasSize(4).isSortedAccordingTo((left, right) -> right - left);
            assertThat(sizes.stream().mapToInt(Integer::intValue).sum()).isEqualTo(14);
            assertThat(sizes).allSatisfy(size -> assertThat(size).isBetween(2, 5));
            assertThat(planner.plan(seed, 4, 14)).isEqualTo(sizes);
            seen.add(sizes);
        }
        assertThat(seen).containsExactlyInAnyOrderElementsOf(planner.candidates(4, 14));
    }

    @Test
    @DisplayName("나눌 수 없는 조합이면 거절한다")
    void 나눌_수_없다() {
        assertThatThrownBy(() -> planner.plan(1L, 3, 16)).isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> planner.plan(1L, 3, 5)).isInstanceOf(IllegalArgumentException.class);
    }
}
