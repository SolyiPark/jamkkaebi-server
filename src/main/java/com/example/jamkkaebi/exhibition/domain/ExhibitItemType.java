package com.example.jamkkaebi.exhibition.domain;

/**
 * 전시관에 놓는 것의 종류와 그리드에서 차지하는 칸 수.
 *
 * <p>배치 좌표 {@code (x, y)} 는 차지하는 칸의 <b>왼쪽 위 칸</b>이다. 가로 {@code width}·세로 {@code height} 칸을 차지하므로
 * {@code x .. x+width-1}, {@code y .. y+height-1} 가 모두 그리드 안이어야 하고, 다른 항목과 칸이 겹치면 안 된다.
 */
public enum ExhibitItemType {

    SPIRIT(1, 1),    // 도깨비(유물): 1×1
    BUILDING(2, 2);  // 건물: 2×2

    private final int width;
    private final int height;

    ExhibitItemType(int width, int height) {
        this.width = width;
        this.height = height;
    }

    /** 가로로 차지하는 칸 수. */
    public int width() {
        return width;
    }

    /** 세로로 차지하는 칸 수. */
    public int height() {
        return height;
    }
}
