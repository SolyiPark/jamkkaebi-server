package com.example.jamkkaebi.friend.spi;

/**
 * 친구에게 보여 줄 전시관의 모습을 만든다.
 * <b>구현은 전시관 도메인</b>({@code ExhibitionViewProvider})이 해서 인터페이스 사용
 */
public interface ExhibitionViewReader {

    // 사용자의 전시관을 친구 공개용으로 만든다.
    ExhibitionView readPublicView(Long userId);

    // 기본 전시관
    ExhibitionView emptyView();
}
