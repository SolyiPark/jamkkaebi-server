package com.example.jamkkaebi.friend.spi;

import java.util.Collection;
import java.util.Map;

/**
 * 사용자들의 도감 진행도(수집률·시대별 복원 수)를 읽는다.
 * <b>유물 도메인이 구현</b>({@code PlayerCollectionProvider}).
 */
public interface PlayerCollectionReader {

    /**
     * @return 사용자 id
     */
    Map<Long, PlayerCollection> readAll(Collection<Long> userIds);
}
