package com.example.jamkkaebi.purification.config;

import com.example.jamkkaebi.relic.domain.AwakeningStage;
import com.example.jamkkaebi.relic.domain.Difficulty;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.boot.context.properties.bind.Binder;
import org.springframework.boot.context.properties.source.MapConfigurationPropertySource;

import java.math.BigDecimal;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 난이도 수치 — 목표 형상을 (형상 개수, 칸 수 합) 으로 정하고, 보드를 만들 수 없는 설정은 기동에서 막는다.
 */
class PurificationPropertiesTest {

    @Test
    @DisplayName("목표 형상의 기본값은 기획서 6-11 의 개수/칸 수를 따른다")
    void 목표_형상_기본값() {
        PurificationProperties properties = defaults();

        assertShape(properties, Difficulty.VERY_EASY, 3, 6);
        assertShape(properties, Difficulty.EASY, 3, 8);
        assertShape(properties, Difficulty.NORMAL, 3, 10);
        assertShape(properties, Difficulty.HARD, 4, 14);
        assertThat(properties.shapeSizeMin()).isEqualTo(2);
        assertThat(properties.shapeSizeMax()).isEqualTo(5);
    }

    @Test
    @DisplayName("칸 수 합을 형상 개수로 나눌 수 없으면 기동에서 거절한다")
    void 나눌_수_없는_칸_수() {
        // 2~5칸짜리 셋으로는 6~15칸만 만들 수 있다.
        assertThatThrownBy(() -> withNormal(params(3, 16)))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("NORMAL");
        assertThatThrownBy(() -> withNormal(params(3, 5)))
                .isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> withNormal(params(0, 0)))
                .isInstanceOf(IllegalArgumentException.class);
    }

    @Test
    @DisplayName("형상·위협·보너스 타일이 보드에 다 들어가지 않으면 기동에서 거절한다")
    void 보드에_들어가지_않는다() {
        // 3×3 = 9칸에 형상 6칸 + 위협 3 + 보너스 2
        DifficultyParams tooSmall = new DifficultyParams(
                3, 3, 3, 3, 6, 19, 3, new BigDecimal("1.2"), 2, null);

        assertThatThrownBy(() -> withNormal(tooSmall))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("보드");
    }

    @Test
    @DisplayName("형상 크기 범위가 뒤집히면 기동에서 거절한다")
    void 형상_크기_범위() {
        assertThatThrownBy(() -> new PurificationProperties(
                null, null, null, null, null, null, null, null, 5, 2, null))
                .isInstanceOf(IllegalArgumentException.class);
    }

    @Test
    @DisplayName("설정 키 shape-cell-total · shape-size-max 로 재배포 없이 바꿀 수 있다")
    void 설정_바인딩() {
        MapConfigurationPropertySource source = new MapConfigurationPropertySource(Map.ofEntries(
                Map.entry("app.purification.shape-size-max", "6"),
                Map.entry("app.purification.difficulties.normal.board-width", "7"),
                Map.entry("app.purification.difficulties.normal.board-height", "8"),
                Map.entry("app.purification.difficulties.normal.threat-count", "6"),
                Map.entry("app.purification.difficulties.normal.shape-count", "3"),
                Map.entry("app.purification.difficulties.normal.shape-cell-total", "12"),
                Map.entry("app.purification.difficulties.normal.tap-budget", "19"),
                Map.entry("app.purification.difficulties.normal.scout-limit", "3"),
                Map.entry("app.purification.difficulties.normal.reward-multiplier", "1.2"),
                Map.entry("app.purification.difficulties.normal.bonus-tile-limit", "3")));

        PurificationProperties properties = new Binder(source)
                .bind("app.purification", PurificationProperties.class).get();

        assertThat(properties.shapeSizeMax()).isEqualTo(6);
        assertShape(properties, Difficulty.NORMAL, 3, 12);
        // 덮어쓰지 않은 난이도는 기본값 그대로다.
        assertShape(properties, Difficulty.HARD, 4, 14);
    }

    @Test
    @DisplayName("난이도의 값 하나만 덮어쓰면 나머지는 그 난이도의 기본값을 쓴다 — 0 으로 채워지지 않는다")
    void 일부만_덮어쓰기() {
        MapConfigurationPropertySource source = new MapConfigurationPropertySource(Map.of(
                "app.purification.difficulties.normal.tap-budget", "21",
                "app.purification.difficulties.hard.unlock-stage", "AWAKENED"));

        PurificationProperties properties = new Binder(source)
                .bind("app.purification", PurificationProperties.class).get();

        DifficultyParams normal = properties.paramsOf(Difficulty.NORMAL);
        assertThat(normal.tapBudget()).isEqualTo(21);
        assertThat(normal.boardWidth()).isEqualTo(7);
        assertThat(normal.boardHeight()).isEqualTo(8);
        assertThat(normal.threatCount()).isEqualTo(6);
        assertShape(properties, Difficulty.NORMAL, 3, 10);
        assertThat(normal.rewardMultiplier()).isEqualByComparingTo("1.2");
        assertThat(normal.unlockStage()).isNull();
        // 어려움을 늘 열어 두려면 해금 단계를 가장 낮은 각성으로 둔다.
        DifficultyParams hard = properties.paramsOf(Difficulty.HARD);
        assertThat(hard.unlockStage()).isEqualTo(AwakeningStage.AWAKENED);
        assertShape(properties, Difficulty.HARD, 4, 14);
    }

    private static void assertShape(PurificationProperties properties, Difficulty difficulty,
                                    int shapeCount, int shapeCellTotal) {
        DifficultyParams params = properties.paramsOf(difficulty);
        assertThat(params.shapeCount()).as("%s 형상 개수", difficulty).isEqualTo(shapeCount);
        assertThat(params.shapeCellTotal()).as("%s 칸 수 합", difficulty).isEqualTo(shapeCellTotal);
    }

    private static DifficultyParams params(int shapeCount, int shapeCellTotal) {
        return new DifficultyParams(7, 8, 6, shapeCount, shapeCellTotal, 19, 3, new BigDecimal("1.2"), 3, null);
    }

    private static PurificationProperties withNormal(DifficultyParams normal) {
        return new PurificationProperties(
                null, null, null, null, null, null, null, null, null, null, Map.of(Difficulty.NORMAL, normal));
    }

    private static PurificationProperties defaults() {
        return new PurificationProperties(null, null, null, null, null, null, null, null, null, null, null);
    }
}
