package com.example.jamkkaebi.purification.dto.response;

import java.util.List;

/**
 * 이번 판의 보드
 *
 * @param shapeCount     목표 형상 수. {@code shapeSizes} 의 길이와 같다
 * @param shapeCellTotal 목표 형상들이 차지하는 칸 수의 합. {@code shapeSizes} 의 합과 같다
 * @param shapeSizes     이번 판의 형상별 칸 수
 */
public record BoardView(
        long seed,
        int width,
        int height,
        int threatCount,
        int shapeCount,
        int shapeCellTotal,
        List<Integer> shapeSizes,
        int tapBudget,
        int scoutLimit
) {
}
