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
    @DisplayName("목표 형상의 기본값은 클라이언트 모양 풀에서 서로 다른 모양으로 만들 수 있는 개수/칸 수다")
    void 목표_형상_기본값() {
        PurificationProperties properties = defaults();

        assertShape(properties, Difficulty.VERY_EASY, 2, 7);
        assertShape(properties, Difficulty.EASY, 3, 10);
        assertShape(properties, Difficulty.NORMAL, 3, 12);
        assertShape(properties, Difficulty.HARD, 4, 14);
        assertThat(properties.shapeSizeMin()).isEqualTo(2);
        assertThat(properties.shapeSizeMax()).isEqualTo(5);
    }

    @Test
    @DisplayName("특수 타일 기본값 — 강화 합은 2 · 2 · 3 · 4칸이고 절반을 형상 안에, 결정 보너스는 1개")
    void 특수_타일_기본값() {
        PurificationProperties properties = defaults();

        assertTiles(properties, Difficulty.VERY_EASY, 2, 1, 1, 1, 1, 1);
        assertTiles(properties, Difficulty.EASY, 4, 1, 1, 1, 1, 1);
        assertTiles(properties, Difficulty.NORMAL, 6, 2, 1, 1, 2, 1);
        assertTiles(properties, Difficulty.HARD, 8, 2, 2, 2, 3, 1);
    }

    @Test
    @DisplayName("탭 예산 기본값은 12 · 16 · 19 · 20 — 보드가 클수록 늘어난다")
    void 탭_예산_기본값() {
        PurificationProperties properties = defaults();

        assertThat(properties.paramsOf(Difficulty.VERY_EASY).tapBudget()).isEqualTo(12);
        assertThat(properties.paramsOf(Difficulty.EASY).tapBudget()).isEqualTo(16);
        assertThat(properties.paramsOf(Difficulty.NORMAL).tapBudget()).isEqualTo(19);
        assertThat(properties.paramsOf(Difficulty.HARD).tapBudget()).isEqualTo(20);
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
                3, 3, 3, 0, 0, 0, 1, 1, 3, 6, 19, 3, new BigDecimal("1.2"), null);

        assertThatThrownBy(() -> withNormal(tooSmall))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("보드");
    }

    @Test
    @DisplayName("도움 타일도 한 칸씩 차지한다")
    void 도움_타일도_칸을_차지한다() {
        // 4×4 = 16칸에 형상 6칸 + 위협 6 + 도움 1 + 보너스 3 = 16칸 — 도움을 하나 더하면 17칸
        withNormal(tiles(6, 0, 0, 1, 2, 1));

        assertThatThrownBy(() -> withNormal(tiles(6, 0, 0, 2, 2, 1)))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("보드");
    }

    @Test
    @DisplayName("형상 안 강화 타일은 형상 칸 수까지만 넣을 수 있다")
    void 형상_안_강화() {
        PurificationProperties properties = withNormal(tiles(3, 6, 0, 1, 1, 1));
        assertThat(properties.paramsOf(Difficulty.NORMAL).reinforcedOnShape()).isEqualTo(6);

        assertThatThrownBy(() -> withNormal(tiles(3, 7, 0, 1, 1, 1)))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("형상 안 강화");
    }

    @Test
    @DisplayName("형상 밖 강화 타일은 일반 칸 수까지 — 보너스 타일과는 같은 칸에 겹칠 수 있다")
    void 형상_밖_강화() {
        // 4×4 = 16칸에서 형상 6 · 위협 3 · 도움 1 을 뺀 일반 칸 6칸. 보너스 3개도 이 6칸에 놓이고 강화와 겹쳐도 된다.
        PurificationProperties properties = withNormal(tiles(3, 0, 6, 1, 2, 1));
        assertThat(properties.paramsOf(Difficulty.NORMAL).reinforcedOnField()).isEqualTo(6);

        assertThatThrownBy(() -> withNormal(tiles(3, 0, 7, 1, 2, 1)))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("형상 밖 강화");
    }

    @Test
    @DisplayName("타일 수가 음수면 기동에서 거절한다")
    void 음수_타일_수() {
        assertThatThrownBy(() -> withNormal(tiles(3, 0, 0, -1, 1, 1)))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("0 이상");
        assertThatThrownBy(() -> withNormal(tiles(3, -1, 0, 1, 1, 1)))
                .isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> withNormal(tiles(3, 0, 0, 1, 2, -1)))
                .isInstanceOf(IllegalArgumentException.class);
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
                Map.entry("app.purification.difficulties.normal.shape-cell-total", "13"),
                Map.entry("app.purification.difficulties.normal.tap-budget", "19"),
                Map.entry("app.purification.difficulties.normal.scout-limit", "3"),
                Map.entry("app.purification.difficulties.normal.reward-multiplier", "1.2"),
                Map.entry("app.purification.difficulties.normal.bonus-jeongseong-count", "2"),
                Map.entry("app.purification.difficulties.normal.bonus-crystal-count", "1")));

        PurificationProperties properties = new Binder(source)
                .bind("app.purification", PurificationProperties.class).get();

        assertThat(properties.shapeSizeMax()).isEqualTo(6);
        assertShape(properties, Difficulty.NORMAL, 3, 13);
        // 덮어쓰지 않은 난이도는 기본값 그대로다.
        assertShape(properties, Difficulty.HARD, 4, 14);
    }

    @Test
    @DisplayName("타일 수는 보드 생성기 명세 §5.1 의 이름을 설정 키로 써서 하나씩 바꿀 수 있다")
    void 타일_수_설정_바인딩() {
        MapConfigurationPropertySource source = new MapConfigurationPropertySource(Map.of(
                "app.purification.difficulties.normal.reinforced-on-shape", "3",
                "app.purification.difficulties.normal.reinforced-on-field", "0",
                "app.purification.difficulties.normal.helper-count", "2",
                "app.purification.difficulties.normal.bonus-jeongseong-count", "3",
                "app.purification.difficulties.normal.bonus-crystal-count", "2"));

        PurificationProperties properties = new Binder(source)
                .bind("app.purification", PurificationProperties.class).get();

        assertTiles(properties, Difficulty.NORMAL, 6, 3, 0, 2, 3, 2);
        assertShape(properties, Difficulty.NORMAL, 3, 12);
        assertTiles(properties, Difficulty.HARD, 8, 2, 2, 2, 3, 1);
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
        assertShape(properties, Difficulty.NORMAL, 3, 12);
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

    private static void assertTiles(PurificationProperties properties, Difficulty difficulty, int threat,
                                    int reinforcedOnShape, int reinforcedOnField, int helper,
                                    int bonusJeongseong, int bonusCrystal) {
        DifficultyParams params = properties.paramsOf(difficulty);
        assertThat(params.threatCount()).as("%s 위협", difficulty).isEqualTo(threat);
        assertThat(params.reinforcedOnShape()).as("%s 형상 안 강화", difficulty).isEqualTo(reinforcedOnShape);
        assertThat(params.reinforcedOnField()).as("%s 형상 밖 강화", difficulty).isEqualTo(reinforcedOnField);
        assertThat(params.helperCount()).as("%s 도움", difficulty).isEqualTo(helper);
        assertThat(params.bonusJeongseongCount()).as("%s 정성 보너스", difficulty).isEqualTo(bonusJeongseong);
        assertThat(params.bonusCrystalCount()).as("%s 결정 보너스", difficulty).isEqualTo(bonusCrystal);
    }

    private static DifficultyParams params(int shapeCount, int shapeCellTotal) {
        return new DifficultyParams(
                7, 8, 6, 2, 1, 1, 2, 1, shapeCount, shapeCellTotal, 19, 3, new BigDecimal("1.2"), null);
    }

    // 4×4 보드에 형상 3개/6칸
    private static DifficultyParams tiles(int threat, int reinforcedOnShape, int reinforcedOnField,
                                          int helper, int bonusJeongseong, int bonusCrystal) {
        return new DifficultyParams(4, 4, threat, reinforcedOnShape, reinforcedOnField, helper,
                bonusJeongseong, bonusCrystal, 3, 6, 19, 3, new BigDecimal("1.2"), null);
    }

    private static PurificationProperties withNormal(DifficultyParams normal) {
        return new PurificationProperties(
                null, null, null, null, null, null, null, null, null, null, Map.of(Difficulty.NORMAL, normal));
    }

    private static PurificationProperties defaults() {
        return new PurificationProperties(null, null, null, null, null, null, null, null, null, null, null);
    }
}
