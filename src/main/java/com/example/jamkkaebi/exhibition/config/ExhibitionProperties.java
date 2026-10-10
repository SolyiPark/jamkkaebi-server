package com.example.jamkkaebi.exhibition.config;

import com.example.jamkkaebi.friend.spi.ExhibitionView;
import org.springframework.boot.context.properties.ConfigurationProperties;

/**
 * 전시관 수치(임시값).
 *
 * <p>배치 좌표 범위는 클라이언트의 아이소메트릭 맵 크기에 묶여 있어 설정으로 둔다.
 * 맵이 확정되면 이 값만 바꾼다.
 *
 * @param gridWidth  가로 칸 수. 좌표 {@code x} 는 {@code 0 ≤ x < gridWidth}
 * @param gridHeight 세로 칸 수. 좌표 {@code y} 는 {@code 0 ≤ y < gridHeight}
 */
@ConfigurationProperties(prefix = "app.exhibition")
public record ExhibitionProperties(
        Integer gridWidth,
        Integer gridHeight
) {

    public ExhibitionProperties {
        gridWidth = orDefault(gridWidth, 20);
        gridHeight = orDefault(gridHeight, 20);
        // 범위 밖 좌표
        if (gridWidth <= 0 || gridHeight <= 0) {
            throw new IllegalArgumentException("전시관 그리드의 가로·세로는 1 이상이어야 합니다.");
        }
    }

    public ExhibitionView.Grid grid() {
        return new ExhibitionView.Grid(gridWidth, gridHeight);
    }

    private static Integer orDefault(Integer value, int defaultValue) {
        return value == null ? defaultValue : value;
    }
}
