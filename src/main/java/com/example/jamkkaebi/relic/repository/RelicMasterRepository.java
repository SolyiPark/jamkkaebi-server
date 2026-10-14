package com.example.jamkkaebi.relic.repository;

import com.example.jamkkaebi.relic.domain.RelicMaster;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.List;

public interface RelicMasterRepository extends JpaRepository<RelicMaster, Integer> {

    List<RelicMaster> findAllByOrderByDisplayOrderAsc();
}
